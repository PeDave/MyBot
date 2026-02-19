using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using Mexc.Net.Clients;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using MexcOrderSide = Mexc.Net.Enums.OrderSide;
using MexcOrderStatus = Mexc.Net.Enums.OrderStatus;
using MexcOrderType = Mexc.Net.Enums.OrderType;

namespace MyBot.Exchanges.Mexc;

/// <summary>WebSocket client for the MEXC cryptocurrency exchange using JK.Mexc.Net SDK.</summary>
public class MexcWebSocketClient : IExchangeWebSocketClient
{
    private readonly MexcSocketClient _socketClient;
    private readonly MexcRestClient _restClient;
    private readonly ILogger<MexcWebSocketClient> _logger;
    private readonly Dictionary<string, UpdateSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _connected;
    private bool _disposed;

    /// <inheritdoc />
    public string ExchangeName => "MEXC";

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
    /// Initializes a new <see cref="MexcWebSocketClient"/> with API credentials.
    /// </summary>
    public MexcWebSocketClient(string apiKey, string apiSecret, ILogger<MexcWebSocketClient> logger)
    {
        _logger = logger;
        var credentials = new ApiCredentials(apiKey, apiSecret);
        _socketClient = new MexcSocketClient(options => { options.ApiCredentials = credentials; });
        _restClient = new MexcRestClient(options => { options.ApiCredentials = credentials; });
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
        var key = "ticker-" + symbol;
        var result = await _socketClient.SpotApi.SubscribeToMiniTickerUpdatesAsync(symbol, data =>
        {
            var t = data.Data;
            OnTickerUpdate?.Invoke(this, new WebSocketTickerUpdate
            {
                Symbol = t.Symbol,
                LastPrice = t.LastPrice,
                High24h = t.HighPrice,
                Low24h = t.LowPrice,
                Volume24h = t.Volume,
                QuoteVolume24h = t.QuoteVolume,
                ChangePercent24h = t.PriceChangePercentage,
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            });
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        var key = "orderbook-" + symbol;
        var result = await _socketClient.SpotApi.SubscribeToPartialOrderBookUpdatesAsync(symbol, depth, data =>
        {
            var book = data.Data;
            OnOrderBookUpdate?.Invoke(this, new WebSocketOrderBookUpdate
            {
                Symbol = data.Symbol ?? symbol,
                Bids = book.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = book.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            });
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToTradesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var key = "trades-" + symbol;
        var result = await _socketClient.SpotApi.SubscribeToTradeUpdatesAsync(symbol, data =>
        {
            foreach (var t in data.Data)
            {
                OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
                {
                    TradeId = string.Empty,
                    Symbol = data.Symbol ?? symbol,
                    Price = t.Price,
                    Quantity = t.Quantity,
                    TakerSide = t.Side == MexcOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
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
        var listenKey = await GetListenKeyAsync(cancellationToken);

        var result = await _socketClient.SpotApi.SubscribeToOrderUpdatesAsync(listenKey, data =>
        {
            var o = data.Data;
            OnOrderUpdate?.Invoke(this, new WebSocketOrderUpdate
            {
                OrderId = o.OrderId ?? string.Empty,
                ClientOrderId = o.ClientOrderId ?? string.Empty,
                Symbol = data.Symbol ?? string.Empty,
                Side = o.Side == MexcOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                Type = o.OrderType == MexcOrderType.Market ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(o.Status),
                Quantity = o.Quantity,
                QuantityFilled = o.CumulativeQuantity ?? 0,
                Price = o.Price == 0 ? null : o.Price,
                AveragePrice = o.AveragePrice,
                Exchange = ExchangeName,
                Timestamp = o.Timestamp
            });
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToUserBalanceAsync(CancellationToken cancellationToken = default)
    {
        const string key = "balance";
        var listenKey = await GetListenKeyAsync(cancellationToken);

        var result = await _socketClient.SpotApi.SubscribeToAccountUpdatesAsync(listenKey, data =>
        {
            var b = data.Data;
            OnBalanceUpdate?.Invoke(this, new WebSocketBalanceUpdate
            {
                Asset = b.Asset,
                Available = b.Free,
                Locked = b.Frozen,
                Total = b.Free + b.Frozen,
                Exchange = ExchangeName,
                Timestamp = b.Timestamp
            });
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

    private async Task<string> GetListenKeyAsync(CancellationToken cancellationToken)
    {
        var keyResult = await _restClient.SpotApi.Account.StartUserStreamAsync(cancellationToken);
        if (!keyResult.Success)
        {
            var msg = keyResult.Error?.Message ?? "Failed to obtain listen key";
            throw new ExchangeWebSocketException(ExchangeName, msg);
        }
        return keyResult.Data;
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

    private static OrderStatus MapOrderStatus(MexcOrderStatus status) => status switch
    {
        MexcOrderStatus.New => OrderStatus.New,
        MexcOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        MexcOrderStatus.Filled => OrderStatus.Filled,
        MexcOrderStatus.Canceled => OrderStatus.Canceled,
        MexcOrderStatus.PartiallyCanceled => OrderStatus.Canceled,
        _ => OrderStatus.Unknown
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        _socketClient.Dispose();
        _restClient.Dispose();
        _lock.Dispose();
    }
}
