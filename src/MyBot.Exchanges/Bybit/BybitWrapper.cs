using Bybit.Net.Clients;
using CryptoExchange.Net.Authentication;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BybitCategory = Bybit.Net.Enums.Category;
using BybitAccountType = Bybit.Net.Enums.AccountType;
using BybitOrderSide = Bybit.Net.Enums.OrderSide;
using BybitNewOrderType = Bybit.Net.Enums.NewOrderType;
using BybitOrderStatus = Bybit.Net.Enums.OrderStatus;
using BybitTimeInForce = Bybit.Net.Enums.TimeInForce;
using BybitKlineInterval = Bybit.Net.Enums.KlineInterval;

namespace MyBot.Exchanges.Bybit;

/// <summary>Wrapper for the Bybit cryptocurrency exchange using Bybit.Net SDK.</summary>
public class BybitWrapper : IExchangeWrapper, IDisposable
{
    private readonly BybitRestClient _client;
    private readonly ILogger<BybitWrapper> _logger;

    public string ExchangeName => "Bybit";

    public BybitWrapper(string apiKey, string apiSecret, ILogger<BybitWrapper> logger)
    {
        _logger = logger;
        _client = new BybitRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
    }

    public async Task<IEnumerable<UnifiedBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.V5Api.Account.GetBalancesAsync(BybitAccountType.Unified, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get balances", result.Error?.Code?.ToString());

            return result.Data.List
                .SelectMany(account => account.Assets)
                .Select(a => new UnifiedBalance
                {
                    Asset = a.Asset,
                    Available = a.Free ?? 0,
                    Locked = a.Locked ?? 0,
                    Total = (a.Free ?? 0) + (a.Locked ?? 0),
                    Exchange = ExchangeName,
                    Timestamp = DateTime.UtcNow
                });
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balances from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<AccountBalances> GetAllAccountBalancesAsync(CancellationToken cancellationToken = default)
    {
        var result = new AccountBalances();
        try
        {
            var unifiedBalances = await GetBalancesAsync(cancellationToken);
            result.Unified = unifiedBalances
                .Where(b => b.Total > 0)
                .Select(b => new AssetBalance
                {
                    Asset = b.Asset,
                    Free = b.Available,
                    Locked = b.Locked,
                    UsdValue = 0
                }).ToList();
            _logger.LogInformation("Bybit UNIFIED: {Count} assets", result.Unified.Count);
            foreach (var asset in result.Unified)
            {
                _logger.LogInformation("Bybit UNIFIED asset: {Asset}, Free: {Free}, Locked: {Locked}",
                    asset.Asset, asset.Free, asset.Locked);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unified balances from {Exchange}", ExchangeName);
        }
        return result;
    }

    public async Task<UnifiedOrder> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity,
        decimal? price = null, TimeInForce timeInForce = TimeInForce.GoodTillCanceled, CancellationToken cancellationToken = default)
    {
        try
        {
            var bySide = side == OrderSide.Buy ? BybitOrderSide.Buy : BybitOrderSide.Sell;
            var byType = type == OrderType.Market ? BybitNewOrderType.Market : BybitNewOrderType.Limit;
            var byTif = MapTimeInForce(timeInForce);

            var result = await _client.V5Api.Trading.PlaceOrderAsync(
                BybitCategory.Spot,
                symbol,
                bySide,
                byType,
                quantity,
                price: price,
                timeInForce: byTif,
                ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to place order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId,
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = symbol,
                Side = side,
                Type = type,
                Quantity = quantity,
                Price = price,
                Status = OrderStatus.New,
                TimeInForce = timeInForce,
                CreateTime = DateTime.UtcNow,
                Exchange = ExchangeName
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing order on {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedOrder> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.V5Api.Trading.CancelOrderAsync(
                BybitCategory.Spot, symbol, orderId, ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to cancel order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId,
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = symbol,
                Status = OrderStatus.Canceled,
                Exchange = ExchangeName
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling order on {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedOrder> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.V5Api.Trading.GetOrdersAsync(
                BybitCategory.Spot, symbol, orderId: orderId, limit: 1, ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order", result.Error?.Code?.ToString());

            var order = result.Data.List.FirstOrDefault()
                ?? throw new ExchangeException(ExchangeName, $"Order {orderId} not found");
            return MapOrder(order);
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<IEnumerable<UnifiedOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // openOnly=0 means "open orders only" per Bybit V5 API spec
            var result = await _client.V5Api.Trading.GetOrdersAsync(
                BybitCategory.Spot, symbol, openOnly: 0, ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get open orders", result.Error?.Code?.ToString());

            return result.Data.List.Select(MapOrder);
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting open orders from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<IEnumerable<UnifiedOrder>> GetOrderHistoryAsync(string? symbol = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.V5Api.Trading.GetOrderHistoryAsync(
                BybitCategory.Spot, symbol, limit: limit, ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order history", result.Error?.Code?.ToString());

            return result.Data.List.Select(MapOrder);
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order history from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedTicker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.V5Api.ExchangeData.GetSpotTickersAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get ticker", result.Error?.Code?.ToString());

            var t = result.Data.List.FirstOrDefault()
                ?? throw new ExchangeException(ExchangeName, $"Ticker for {symbol} not found");

            return new UnifiedTicker
            {
                Symbol = t.Symbol,
                LastPrice = t.LastPrice,
                BidPrice = t.BestBidPrice ?? 0,
                AskPrice = t.BestAskPrice ?? 0,
                High24h = t.HighPrice24h,
                Low24h = t.LowPrice24h,
                Volume24h = t.Volume24h,
                QuoteVolume24h = t.Turnover24h,
                ChangePercent24h = t.PriceChangePercentag24h,
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ticker from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedOrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.V5Api.ExchangeData.GetOrderbookAsync(BybitCategory.Spot, symbol, limit: depth, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order book", result.Error?.Code?.ToString());

            return new UnifiedOrderBook
            {
                Symbol = result.Data.Symbol,
                Bids = result.Data.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = result.Data.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = result.Data.Timestamp
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order book from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<IEnumerable<UnifiedTrade>> GetRecentTradesAsync(string symbol, int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.V5Api.ExchangeData.GetTradeHistoryAsync(
                BybitCategory.Spot, symbol, limit: limit, ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get recent trades", result.Error?.Code?.ToString());

            return result.Data.List.Select(t => new UnifiedTrade
            {
                TradeId = t.TradeId,
                Symbol = t.Symbol,
                Price = t.Price,
                Quantity = t.Quantity,
                QuoteQuantity = t.Price * t.Quantity,
                TakerSide = t.Side == BybitOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                Timestamp = t.Timestamp,
                Exchange = ExchangeName
            });
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent trades from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    private UnifiedOrder MapOrder(global::Bybit.Net.Objects.Models.V5.BybitOrder order) => new()
    {
        OrderId = order.OrderId,
        ClientOrderId = order.ClientOrderId,
        Symbol = order.Symbol,
        Side = order.Side == BybitOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
        Type = order.OrderType == global::Bybit.Net.Enums.OrderType.Market ? OrderType.Market : OrderType.Limit,
        Status = MapOrderStatus(order.Status),
        Quantity = order.Quantity,
        QuantityFilled = order.QuantityFilled ?? 0,
        Price = order.Price,
        AveragePrice = order.AveragePrice,
        QuoteQuantityFilled = order.ValueFilled,
        TimeInForce = MapBybitTimeInForce(order.TimeInForce),
        CreateTime = order.CreateTime,
        UpdateTime = order.UpdateTime,
        Exchange = ExchangeName
    };

    private static OrderStatus MapOrderStatus(BybitOrderStatus status) => status switch
    {
        BybitOrderStatus.New => OrderStatus.New,
        BybitOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        BybitOrderStatus.Filled => OrderStatus.Filled,
        BybitOrderStatus.Cancelled => OrderStatus.Canceled,
        BybitOrderStatus.Rejected => OrderStatus.Rejected,
        BybitOrderStatus.PartiallyFilledCanceled => OrderStatus.Canceled,
        _ => OrderStatus.Unknown
    };

    private static BybitTimeInForce MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.ImmediateOrCancel => BybitTimeInForce.ImmediateOrCancel,
        TimeInForce.FillOrKill => BybitTimeInForce.FillOrKill,
        _ => BybitTimeInForce.GoodTillCanceled
    };

    private static TimeInForce MapBybitTimeInForce(BybitTimeInForce tif) => tif switch
    {
        BybitTimeInForce.ImmediateOrCancel => TimeInForce.ImmediateOrCancel,
        BybitTimeInForce.FillOrKill => TimeInForce.FillOrKill,
        _ => TimeInForce.GoodTillCanceled
    };

    public async Task<IEnumerable<UnifiedKline>> GetKlinesAsync(string symbol, string timeframe, DateTime startTime, DateTime endTime, int limit = 200, CancellationToken cancellationToken = default)
    {
        try
        {
            var interval = timeframe switch
            {
                "1m" => BybitKlineInterval.OneMinute,
                "5m" => BybitKlineInterval.FiveMinutes,
                "15m" => BybitKlineInterval.FifteenMinutes,
                "30m" => BybitKlineInterval.ThirtyMinutes,
                "1h" => BybitKlineInterval.OneHour,
                "4h" => BybitKlineInterval.FourHours,
                "1d" => BybitKlineInterval.OneDay,
                _ => throw new ArgumentException($"Unsupported timeframe: {timeframe}")
            };
            var result = await _client.V5Api.ExchangeData.GetKlinesAsync(BybitCategory.Spot, symbol, interval, startTime, endTime, limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get klines", result.Error?.Code?.ToString());
            return result.Data.List
                .Where(k => k.StartTime >= startTime && k.StartTime <= endTime)
                .Select(k => new UnifiedKline
                {
                    OpenTime = k.StartTime,
                    Open = k.OpenPrice,
                    High = k.HighPrice,
                    Low = k.LowPrice,
                    Close = k.ClosePrice,
                    Volume = k.Volume,
                    QuoteVolume = k.QuoteVolume,
                    Symbol = symbol,
                    Exchange = ExchangeName
                });
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting klines from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public void Dispose() => _client.Dispose();}
