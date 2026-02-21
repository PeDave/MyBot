using Bitget.Net.Clients;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets.Default;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BitgetOrderSide = Bitget.Net.Enums.V2.OrderSide;
using BitgetOrderStatus = Bitget.Net.Enums.V2.OrderStatus;
using BitgetOrderType = Bitget.Net.Enums.V2.OrderType;

namespace MyBot.Exchanges.Bitget;

/// <summary>WebSocket client for the Bitget cryptocurrency exchange using JK.Bitget.Net SDK.</summary>
public class BitgetWebSocketClient : IExchangeWebSocketClient
{
    private readonly BitgetSocketClient _socketClient;
    private readonly ILogger<BitgetWebSocketClient> _logger;
    private readonly Dictionary<string, UpdateSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string ExchangeName => "Bitget";

    /// <inheritdoc/>
    public bool IsConnected => _subscriptions.Values.Any(s => s.SocketStatus == SocketStatus.Connected);

    /// <inheritdoc/>
    public event EventHandler<WebSocketTickerUpdate>? OnTickerUpdate;

    /// <inheritdoc/>
    public event EventHandler<WebSocketOrderBookUpdate>? OnOrderBookUpdate;

    /// <inheritdoc/>
    public event EventHandler<WebSocketTradeUpdate>? OnTradeUpdate;

    /// <inheritdoc/>
    public event EventHandler<WebSocketOrderUpdate>? OnOrderUpdate;

    /// <inheritdoc/>
    public event EventHandler<WebSocketBalanceUpdate>? OnBalanceUpdate;

    /// <inheritdoc/>
    public event EventHandler<string>? OnError;

    /// <summary>Creates a Bitget WebSocket client for public streams (no authentication required).</summary>
    public BitgetWebSocketClient(ILogger<BitgetWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BitgetSocketClient();
    }

    /// <summary>Creates a Bitget WebSocket client with authentication for private streams.</summary>
    public BitgetWebSocketClient(string apiKey, string apiSecret, string passphrase, ILogger<BitgetWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BitgetSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret, passphrase);
        });
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // The Bitget SDK connects automatically on first subscription.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var sub in _subscriptions.Values)
                await sub.CloseAsync();
            _subscriptions.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeToTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        const string streamPrefix = "ticker_";
        var streamName = streamPrefix + symbol;
        try
        {
            var result = await _socketClient.SpotApiV2.SubscribeToTickerUpdatesAsync(
                symbol,
                data =>
                {
                    foreach (var t in data.Data)
                    {
                        OnTickerUpdate?.Invoke(this, new WebSocketTickerUpdate
                        {
                            Symbol = t.Symbol,
                            LastPrice = t.LastPrice,
                            BidPrice = t.BestBidPrice,
                            AskPrice = t.BestAskPrice,
                            High24h = t.HighPrice24h,
                            Low24h = t.LowPrice24h,
                            Volume24h = t.BaseVolume,
                            QuoteVolume24h = t.QuoteVolume,
                            ChangePercent24h = t.ChangePercentage,
                            Exchange = ExchangeName,
                            Timestamp = t.Timestamp
                        });
                    }
                },
                cancellationToken);

            if (!result.Success)
                throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, result.Error?.Message ?? "Subscription failed");

            await AddSubscriptionAsync(streamName, result.Data);
        }
        catch (ExchangeWebSocketException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to ticker on {Exchange} for {Symbol}", ExchangeName, symbol);
            OnError?.Invoke(this, ex.Message);
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeToOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        var streamName = $"orderbook_{symbol}";
        try
        {
            var result = await _socketClient.SpotApiV2.SubscribeToOrderBookUpdatesAsync(
                symbol,
                depth,
                data =>
                {
                    foreach (var ob in data.Data)
                    {
                        OnOrderBookUpdate?.Invoke(this, new WebSocketOrderBookUpdate
                        {
                            Symbol = symbol,
                            Bids = ob.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                            Asks = ob.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                            Exchange = ExchangeName,
                            Timestamp = ob.Timestamp
                        });
                    }
                },
                cancellationToken);

            if (!result.Success)
                throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, result.Error?.Message ?? "Subscription failed");

            await AddSubscriptionAsync(streamName, result.Data);
        }
        catch (ExchangeWebSocketException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to order book on {Exchange} for {Symbol}", ExchangeName, symbol);
            OnError?.Invoke(this, ex.Message);
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeToTradesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var streamName = $"trades_{symbol}";
        try
        {
            var result = await _socketClient.SpotApiV2.SubscribeToTradeUpdatesAsync(
                symbol,
                data =>
                {
                    foreach (var t in data.Data)
                    {
                        OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
                        {
                            TradeId = t.TradeId,
                            Symbol = symbol,
                            Price = t.Price,
                            Quantity = t.Quantity,
                            QuoteQuantity = t.Price * t.Quantity,
                            TakerSide = t.Side == BitgetOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                            Exchange = ExchangeName,
                            Timestamp = t.Timestamp
                        });
                    }
                },
                cancellationToken);

            if (!result.Success)
                throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, result.Error?.Message ?? "Subscription failed");

            await AddSubscriptionAsync(streamName, result.Data);
        }
        catch (ExchangeWebSocketException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to trades on {Exchange} for {Symbol}", ExchangeName, symbol);
            OnError?.Invoke(this, ex.Message);
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeToUserOrdersAsync(CancellationToken cancellationToken = default)
    {
        const string streamName = "user_orders";
        try
        {
            var result = await _socketClient.SpotApiV2.SubscribeToOrderUpdatesAsync(
                data =>
                {
                    foreach (var o in data.Data)
                    {
                        OnOrderUpdate?.Invoke(this, new WebSocketOrderUpdate
                        {
                            OrderId = o.OrderId,
                            ClientOrderId = o.ClientOrderId,
                            Symbol = o.Symbol,
                            Side = o.Side == BitgetOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                            Type = o.OrderType == BitgetOrderType.Market ? OrderType.Market : OrderType.Limit,
                            Status = MapOrderStatus(o.Status),
                            Quantity = o.Quantity,
                            QuantityFilled = o.QuantityFilled ?? 0,
                            Price = o.Price,
                            AveragePrice = o.AveragePrice,
                            Exchange = ExchangeName,
                            Timestamp = o.UpdateTime ?? o.CreateTime
                        });
                    }
                },
                cancellationToken);

            if (!result.Success)
                throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, result.Error?.Message ?? "Subscription failed");

            await AddSubscriptionAsync(streamName, result.Data);
        }
        catch (ExchangeWebSocketException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to user orders on {Exchange}", ExchangeName);
            OnError?.Invoke(this, ex.Message);
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeToUserBalanceAsync(CancellationToken cancellationToken = default)
    {
        const string streamName = "user_balance";
        try
        {
            var result = await _socketClient.SpotApiV2.SubscribeToBalanceUpdatesAsync(
                data =>
                {
                    foreach (var b in data.Data)
                    {
                        OnBalanceUpdate?.Invoke(this, new WebSocketBalanceUpdate
                        {
                            Asset = b.Asset,
                            Available = b.Available,
                            Locked = (b.Frozen ?? 0) + (b.Locked ?? 0),
                            Total = b.Available + (b.Frozen ?? 0) + (b.Locked ?? 0),
                            Exchange = ExchangeName,
                            Timestamp = b.UpdateTime
                        });
                    }
                },
                cancellationToken);

            if (!result.Success)
                throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, result.Error?.Message ?? "Subscription failed");

            await AddSubscriptionAsync(streamName, result.Data);
        }
        catch (ExchangeWebSocketException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to user balance on {Exchange}", ExchangeName);
            OnError?.Invoke(this, ex.Message);
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task UnsubscribeAsync(string streamName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_subscriptions.TryGetValue(streamName, out var sub))
            {
                await sub.CloseAsync();
                _subscriptions.Remove(streamName);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task AddSubscriptionAsync(string streamName, UpdateSubscription subscription)
    {
        await _lock.WaitAsync();
        try
        {
            if (_subscriptions.TryGetValue(streamName, out var existing))
            {
                await existing.CloseAsync();
            }
            _subscriptions[streamName] = subscription;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static OrderStatus MapOrderStatus(BitgetOrderStatus status) => status switch
    {
        BitgetOrderStatus.New => OrderStatus.New,
        BitgetOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        BitgetOrderStatus.Filled => OrderStatus.Filled,
        BitgetOrderStatus.Canceled => OrderStatus.Canceled,
        BitgetOrderStatus.Rejected => OrderStatus.Rejected,
        _ => OrderStatus.Unknown
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _socketClient.Dispose();
        _lock.Dispose();
    }
}
