using BingX.Net.Clients;
using CryptoExchange.Net.Authentication;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BingXOrderSide = BingX.Net.Enums.OrderSide;
using BingXOrderType = BingX.Net.Enums.OrderType;
using BingXOrderStatus = BingX.Net.Enums.OrderStatus;
using BingXTimeInForce = BingX.Net.Enums.TimeInForce;
using BingXOrderDetails = BingX.Net.Objects.Models.BingXOrderDetails;

namespace MyBot.Exchanges.BingX;

/// <summary>Wrapper for the BingX cryptocurrency exchange using JK.BingX.Net SDK.</summary>
public class BingXWrapper : IExchangeWrapper, IDisposable
{
    private readonly BingXRestClient _client;
    private readonly ILogger<BingXWrapper> _logger;

    public string ExchangeName => "BingX";

    public BingXWrapper(string apiKey, string apiSecret, ILogger<BingXWrapper> logger)
    {
        _logger = logger;
        _client = new BingXRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
    }

    public async Task<IEnumerable<UnifiedBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApi.Account.GetBalancesAsync(ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get balances", result.Error?.Code?.ToString());

            return result.Data.Select(b => new UnifiedBalance
            {
                Asset = b.Asset,
                Available = b.Free,
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
            var bxSide = side == OrderSide.Buy ? BingXOrderSide.Buy : BingXOrderSide.Sell;
            var bxType = type == OrderType.Market ? BingXOrderType.Market : BingXOrderType.Limit;
            var bxTif = MapTimeInForce(timeInForce);

            var result = await _client.SpotApi.Trading.PlaceOrderAsync(
                symbol, bxSide, bxType,
                quantity: quantity,
                price: price,
                timeInForce: bxTif,
                ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to place order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = symbol,
                Side = side,
                Type = type,
                Quantity = quantity,
                Price = price,
                Status = MapOrderStatus(result.Data.Status),
                TimeInForce = timeInForce,
                CreateTime = result.Data.Timestamp,
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
            var longOrderId = long.TryParse(orderId, out var parsed) ? (long?)parsed : null;
            var result = await _client.SpotApi.Trading.CancelOrderAsync(symbol, longOrderId, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to cancel order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = result.Data.Symbol,
                Side = result.Data.Side == BingXOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                Status = MapOrderStatus(result.Data.Status),
                Quantity = result.Data.Quantity,
                QuantityFilled = result.Data.QuantityFilled,
                Price = result.Data.Price,
                CreateTime = result.Data.Timestamp,
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
            var longOrderId = long.TryParse(orderId, out var parsed) ? (long?)parsed : null;
            var result = await _client.SpotApi.Trading.GetOrderAsync(symbol, longOrderId, ct: cancellationToken);
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
            var result = await _client.SpotApi.Trading.GetOrdersAsync(symbol, pageSize: limit, ct: cancellationToken);
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
            var result = await _client.SpotApi.ExchangeData.GetTickersAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get ticker", result.Error?.Code?.ToString());

            var t = result.Data.FirstOrDefault()
                ?? throw new ExchangeException(ExchangeName, $"Ticker for {symbol} not found");

            decimal changePercent = 0;
            if (decimal.TryParse(t.PriceChangePercent?.TrimEnd('%'), out var pct))
                changePercent = pct;

            return new UnifiedTicker
            {
                Symbol = t.Symbol,
                LastPrice = t.LastPrice,
                BidPrice = t.BestBidPrice,
                AskPrice = t.BestAskPrice,
                High24h = t.HighPrice,
                Low24h = t.LowPrice,
                Volume24h = t.Volume,
                QuoteVolume24h = t.QuoteVolume,
                ChangePercent24h = changePercent,
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
                Symbol = result.Data.Symbol ?? symbol,
                Bids = result.Data.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = result.Data.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = result.Data.Timestamp ?? DateTime.UtcNow
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

            return result.Data.Select(t => new UnifiedTrade
            {
                TradeId = t.Id.ToString(),
                Symbol = symbol,
                Price = t.Price,
                Quantity = t.Quantity,
                QuoteQuantity = t.Price * t.Quantity,
                TakerSide = t.BuyerIsMaker ? OrderSide.Sell : OrderSide.Buy,
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

    private UnifiedOrder MapOrder(BingXOrderDetails order) => new()
    {
        OrderId = order.OrderId.ToString(),
        ClientOrderId = order.ClientOrderId,
        Symbol = order.Symbol,
        Side = order.Side == BingXOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
        Type = MapOrderType(order.Type),
        Status = MapOrderStatus(order.Status),
        Quantity = order.Quantity,
        QuantityFilled = order.QuantityFilled,
        Price = order.Price,
        AveragePrice = order.AveragePrice,
        QuoteQuantityFilled = order.QuoteQuantity,
        CreateTime = order.CreateTime,
        UpdateTime = order.UpdateTime,
        Exchange = ExchangeName
    };

    private static OrderStatus MapOrderStatus(BingXOrderStatus status) => status switch
    {
        BingXOrderStatus.New => OrderStatus.New,
        BingXOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        BingXOrderStatus.Filled => OrderStatus.Filled,
        BingXOrderStatus.Canceled => OrderStatus.Canceled,
        BingXOrderStatus.Failed => OrderStatus.Rejected,
        _ => OrderStatus.Unknown
    };

    private static OrderType MapOrderType(BingXOrderType type) => type switch
    {
        BingXOrderType.Market => OrderType.Market,
        _ => OrderType.Limit
    };

    private static BingXTimeInForce MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.ImmediateOrCancel => BingXTimeInForce.ImmediateOrCancel,
        TimeInForce.FillOrKill => BingXTimeInForce.FillOrKill,
        _ => BingXTimeInForce.GoodTillCanceled
    };

    public void Dispose() => _client.Dispose();
}
