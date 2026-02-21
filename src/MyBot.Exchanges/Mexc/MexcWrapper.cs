using CryptoExchange.Net.Authentication;
using Mexc.Net.Clients;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using MexcOrderSide = Mexc.Net.Enums.OrderSide;
using MexcOrderType = Mexc.Net.Enums.OrderType;
using MexcOrderStatus = Mexc.Net.Enums.OrderStatus;
using MexcTimeInForce = Mexc.Net.Enums.TimeInForce;
using MexcOrder = Mexc.Net.Objects.Models.Spot.MexcOrder;
using MexcKlineInterval = Mexc.Net.Enums.KlineInterval;

namespace MyBot.Exchanges.Mexc;

/// <summary>Wrapper for the MEXC cryptocurrency exchange using JK.Mexc.Net SDK.</summary>
public class MexcWrapper : IExchangeWrapper, IDisposable
{
    private readonly MexcRestClient _client;
    private readonly ILogger<MexcWrapper> _logger;

    public string ExchangeName => "MEXC";

    public MexcWrapper(string apiKey, string apiSecret, ILogger<MexcWrapper> logger)
    {
        _logger = logger;
        _client = new MexcRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
    }

    public async Task<IEnumerable<UnifiedBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApi.Account.GetAccountInfoAsync(ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get balances", result.Error?.Code?.ToString());

            return result.Data.Balances.Select(b => new UnifiedBalance
            {
                Asset = b.Asset,
                Available = b.Available,
                Locked = b.Locked,
                Total = b.Total,
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

    public async Task<UnifiedOrder> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity,
        decimal? price = null, TimeInForce timeInForce = TimeInForce.GoodTillCanceled, CancellationToken cancellationToken = default)
    {
        try
        {
            var mxSide = side == OrderSide.Buy ? MexcOrderSide.Buy : MexcOrderSide.Sell;
            var mxType = type == OrderType.Market ? MexcOrderType.Market : MexcOrderType.Limit;

            var result = await _client.SpotApi.Trading.PlaceOrderAsync(
                symbol, mxSide, mxType,
                quantity: quantity,
                price: price,
                ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to place order", result.Error?.Code?.ToString());

            return MapOrder(result.Data);
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
            var result = await _client.SpotApi.Trading.CancelOrderAsync(symbol, orderId, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to cancel order", result.Error?.Code?.ToString());

            return MapOrder(result.Data);
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
            var result = await _client.SpotApi.Trading.GetOrderAsync(symbol, orderId, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order", result.Error?.Code?.ToString());

            return MapOrder(result.Data);
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
            var result = await _client.SpotApi.Trading.GetOpenOrdersAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get open orders", result.Error?.Code?.ToString());

            return result.Data.Select(MapOrder);
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
            // MEXC GetOrdersAsync requires a non-null symbol; return empty when none provided
            if (symbol is null)
                return Enumerable.Empty<UnifiedOrder>();
            var result = await _client.SpotApi.Trading.GetOrdersAsync(symbol, limit: limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order history", result.Error?.Code?.ToString());

            return result.Data.Select(MapOrder);
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
            var result = await _client.SpotApi.ExchangeData.GetTickerAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get ticker", result.Error?.Code?.ToString());

            var t = result.Data;
            return new UnifiedTicker
            {
                Symbol = t.Symbol,
                LastPrice = t.LastPrice,
                BidPrice = t.BestBidPrice,
                AskPrice = t.BestAskPrice,
                High24h = t.HighPrice,
                Low24h = t.LowPrice,
                Volume24h = t.Volume,
                QuoteVolume24h = t.QuoteVolume ?? 0,
                ChangePercent24h = t.PriceChangePercentage,
                Exchange = ExchangeName,
                Timestamp = t.CloseTime
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
            var result = await _client.SpotApi.ExchangeData.GetOrderBookAsync(symbol, limit: depth, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order book", result.Error?.Code?.ToString());

            return new UnifiedOrderBook
            {
                Symbol = symbol,
                Bids = result.Data.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = result.Data.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
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
            var result = await _client.SpotApi.ExchangeData.GetRecentTradesAsync(symbol, limit: limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get recent trades", result.Error?.Code?.ToString());

            return result.Data.Select((t, i) => new UnifiedTrade
            {
                TradeId = i.ToString(),
                Symbol = symbol,
                Price = t.Price,
                Quantity = t.Quantity,
                QuoteQuantity = t.QuoteQuantity,
                TakerSide = t.IsBuyerMaker ? OrderSide.Sell : OrderSide.Buy,
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

    private UnifiedOrder MapOrder(MexcOrder order) => new()
    {
        OrderId = order.OrderId,
        ClientOrderId = order.ClientOrderId,
        Symbol = order.Symbol,
        Side = order.Side == MexcOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
        Type = MapOrderType(order.OrderType),
        Status = MapOrderStatus(order.Status),
        Quantity = order.Quantity,
        QuantityFilled = order.QuantityFilled,
        Price = order.Price == 0 ? null : order.Price,
        QuoteQuantityFilled = order.QuoteQuantityFilled,
        TimeInForce = MapTimeInForce(order.TimeInForce),
        CreateTime = order.Timestamp,
        UpdateTime = order.UpdateTime,
        Exchange = ExchangeName
    };

    private static OrderStatus MapOrderStatus(MexcOrderStatus status) => status switch
    {
        MexcOrderStatus.New => OrderStatus.New,
        MexcOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        MexcOrderStatus.Filled => OrderStatus.Filled,
        MexcOrderStatus.Canceled => OrderStatus.Canceled,
        MexcOrderStatus.PartiallyCanceled => OrderStatus.Canceled,
        _ => OrderStatus.Unknown
    };

    private static OrderType MapOrderType(MexcOrderType type) => type switch
    {
        MexcOrderType.Market => OrderType.Market,
        _ => OrderType.Limit
    };

    private static TimeInForce MapTimeInForce(MexcTimeInForce? tif) => tif switch
    {
        MexcTimeInForce.ImmediateOrCancel => TimeInForce.ImmediateOrCancel,
        MexcTimeInForce.FillOrKill => TimeInForce.FillOrKill,
        _ => TimeInForce.GoodTillCanceled
    };

    public async Task<IEnumerable<UnifiedKline>> GetKlinesAsync(string symbol, string timeframe, DateTime startTime, DateTime endTime, int limit = 200, CancellationToken cancellationToken = default)
    {
        try
        {
            var interval = timeframe switch
            {
                "1m" => MexcKlineInterval.OneMinute,
                "5m" => MexcKlineInterval.FiveMinutes,
                "15m" => MexcKlineInterval.FifteenMinutes,
                "30m" => MexcKlineInterval.ThirtyMinutes,
                "1h" => MexcKlineInterval.OneHour,
                "4h" => MexcKlineInterval.FourHours,
                "1d" => MexcKlineInterval.OneDay,
                _ => throw new ArgumentException($"Unsupported timeframe: {timeframe}")
            };
            var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, interval, startTime, endTime, limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get klines", result.Error?.Code?.ToString());
            return result.Data.Select(k => new UnifiedKline
            {
                OpenTime = k.OpenTime,
                Open = k.OpenPrice,
                High = k.HighPrice,
                Low = k.LowPrice,
                Close = k.ClosePrice,
                Volume = k.Volume,
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
