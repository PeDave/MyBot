using Bitget.Net.Clients;
using Bitget.Net.Enums;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
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
    private bool _connected;
    private bool _disposed;

    /// <inheritdoc />
    public string ExchangeName => "Bitget";

    /// <inheritdoc />
    public bool IsConnected => _connected && _subscriptions.Count > 0;

    /// <inheritdoc />
    public event EventHandler<WebSocketTickerUpdate>? OnTickerUpdate;

    /// <inheritdoc />
    public event EventHandler<WebSocketOrderBookUpdate>? OnOrderBookUpdate;

    /// <inheritdoc />
    public event EventHandler<WebSocketTradeUpdate>? OnTradeUpdate;

    /// <inheritdoc />
    public event EventHandler<WebSocketOrderUpdate>? OnOrderUpdate;

    /// <inheritdoc />
    public event EventHandler<WebSocketBalanceUpdate>? OnBalanceUpdate;

    /// <inheritdoc />
    public event EventHandler<string>? OnError;

    /// <summary>
    /// Initializes a new <see cref="BitgetWebSocketClient"/> with API credentials.
    /// </summary>
    public BitgetWebSocketClient(string apiKey, string apiSecret, string passphrase, ILogger<BitgetWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BitgetSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret, passphrase);
        });
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var sub in _subscriptions.Values)
                await sub.CloseAsync();
            _subscriptions.Clear();
            _connected = false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SubscribeToTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        const string prefix = "ticker-";
        var key = prefix + symbol;
        var result = await _socketClient.SpotApiV2.SubscribeToTickerUpdatesAsync(symbol, data =>
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
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        var key = "orderbook-" + symbol;
        var result = await _socketClient.SpotApiV2.SubscribeToOrderBookUpdatesAsync(symbol, depth, data =>
        {
            foreach (var book in data.Data)
            {
                OnOrderBookUpdate?.Invoke(this, new WebSocketOrderBookUpdate
                {
                    Symbol = data.Symbol ?? symbol,
                    Bids = book.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                    Asks = book.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                    Exchange = ExchangeName,
                    Timestamp = book.Timestamp
                });
            }
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToTradesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var key = "trades-" + symbol;
        var result = await _socketClient.SpotApiV2.SubscribeToTradeUpdatesAsync(symbol, data =>
        {
            foreach (var t in data.Data)
            {
                OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
                {
                    TradeId = t.TradeId,
                    Symbol = data.Symbol ?? symbol,
                    Price = t.Price,
                    Quantity = t.Quantity,
                    TakerSide = t.Side == BitgetOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                    Exchange = ExchangeName,
                    Timestamp = t.Timestamp
                });
            }
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToUserOrdersAsync(CancellationToken cancellationToken = default)
    {
        const string key = "orders";
        var result = await _socketClient.SpotApiV2.SubscribeToOrderUpdatesAsync(data =>
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
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToUserBalanceAsync(CancellationToken cancellationToken = default)
    {
        const string key = "balance";
        var result = await _socketClient.SpotApiV2.SubscribeToBalanceUpdatesAsync(data =>
        {
            foreach (var b in data.Data)
            {
                var locked = (b.Frozen ?? 0) + (b.Locked ?? 0);
                OnBalanceUpdate?.Invoke(this, new WebSocketBalanceUpdate
                {
                    Asset = b.Asset,
                    Available = b.Available,
                    Locked = locked,
                    Total = b.Available + locked,
                    Exchange = ExchangeName,
                    Timestamp = b.UpdateTime
                });
            }
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync(string streamName)
    {
        await _lock.WaitAsync();
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

    private async Task StoreSubscriptionAsync(string key, CryptoExchange.Net.Objects.CallResult<UpdateSubscription> result, CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            var msg = result.Error?.Message ?? "Subscription failed";
            _logger.LogError("Failed to subscribe to {Stream} on {Exchange}: {Error}", key, ExchangeName, msg);
            OnError?.Invoke(this, $"{key}: {msg}");
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, key, msg);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_subscriptions.TryGetValue(key, out var existing))
                await existing.CloseAsync();
            _subscriptions[key] = result.Data;
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        _socketClient.Dispose();
        _lock.Dispose();
    }
}
