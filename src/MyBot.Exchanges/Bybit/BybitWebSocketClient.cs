using Bybit.Net.Clients;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets.Default;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BybitOrderSide = Bybit.Net.Enums.OrderSide;
using BybitOrderStatus = Bybit.Net.Enums.OrderStatus;

namespace MyBot.Exchanges.Bybit;

/// <summary>WebSocket client for the Bybit cryptocurrency exchange using Bybit.Net SDK.</summary>
public class BybitWebSocketClient : IExchangeWebSocketClient
{
    private readonly BybitSocketClient _socketClient;
    private readonly ILogger<BybitWebSocketClient> _logger;
    private readonly Dictionary<string, UpdateSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string ExchangeName => "Bybit";

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

    /// <summary>Creates a Bybit WebSocket client for public streams (no authentication required).</summary>
    public BybitWebSocketClient(ILogger<BybitWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BybitSocketClient();
    }

    /// <summary>Creates a Bybit WebSocket client with authentication for private streams.</summary>
    public BybitWebSocketClient(string apiKey, string apiSecret, ILogger<BybitWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BybitSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // The Bybit SDK connects automatically on first subscription.
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
        var streamName = $"ticker_{symbol}";
        try
        {
            var result = await _socketClient.V5SpotApi.SubscribeToTickerUpdatesAsync(
                symbol,
                data =>
                {
                    var t = data.Data;
                    OnTickerUpdate?.Invoke(this, new WebSocketTickerUpdate
                    {
                        Symbol = t.Symbol,
                        LastPrice = t.LastPrice,
                        High24h = t.HighPrice24h,
                        Low24h = t.LowPrice24h,
                        Volume24h = t.Volume24h,
                        QuoteVolume24h = t.Turnover24h,
                        ChangePercent24h = t.PricePercentage24h,
                        Exchange = ExchangeName,
                        Timestamp = DateTime.UtcNow
                    });
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
            var result = await _socketClient.V5SpotApi.SubscribeToOrderbookUpdatesAsync(
                symbol,
                depth,
                data =>
                {
                    var ob = data.Data;
                    OnOrderBookUpdate?.Invoke(this, new WebSocketOrderBookUpdate
                    {
                        Symbol = ob.Symbol,
                        Bids = ob.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                        Asks = ob.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                        Exchange = ExchangeName,
                        Timestamp = ob.Timestamp
                    });
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
            var result = await _socketClient.V5SpotApi.SubscribeToTradeUpdatesAsync(
                symbol,
                data =>
                {
                    foreach (var t in data.Data)
                    {
                        OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
                        {
                            TradeId = t.TradeId,
                            Symbol = t.Symbol,
                            Price = t.Price,
                            Quantity = t.Quantity,
                            QuoteQuantity = t.Price * t.Quantity,
                            TakerSide = t.Side == BybitOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
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
            var result = await _socketClient.V5PrivateApi.SubscribeToOrderUpdatesAsync(
                data =>
                {
                    foreach (var o in data.Data)
                    {
                        OnOrderUpdate?.Invoke(this, new WebSocketOrderUpdate
                        {
                            OrderId = o.OrderId,
                            ClientOrderId = o.ClientOrderId,
                            Symbol = o.Symbol,
                            Side = o.Side == BybitOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                            Type = o.OrderType == global::Bybit.Net.Enums.OrderType.Market ? OrderType.Market : OrderType.Limit,
                            Status = MapOrderStatus(o.Status),
                            Quantity = o.Quantity,
                            QuantityFilled = o.QuantityFilled ?? 0,
                            Price = o.Price,
                            AveragePrice = o.AveragePrice,
                            Exchange = ExchangeName,
                            Timestamp = o.UpdateTime
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
            var result = await _socketClient.V5PrivateApi.SubscribeToWalletUpdatesAsync(
                data =>
                {
                    foreach (var account in data.Data)
                    {
                        if (account.Assets is null) continue;
                        foreach (var a in account.Assets)
                        {
                            OnBalanceUpdate?.Invoke(this, new WebSocketBalanceUpdate
                            {
                                Asset = a.Asset,
                                Available = a.Free ?? 0,
                                Locked = a.Locked ?? 0,
                                Total = (a.Free ?? 0) + (a.Locked ?? 0),
                                Exchange = ExchangeName,
                                Timestamp = DateTime.UtcNow
                            });
                        }
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
                await existing.CloseAsync();
            _subscriptions[streamName] = subscription;
        }
        finally
        {
            _lock.Release();
        }
    }

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

    /// <inheritdoc/>
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _socketClient.Dispose();
        _lock.Dispose();
    }
}
