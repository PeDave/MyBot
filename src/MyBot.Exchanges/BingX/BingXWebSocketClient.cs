using BingX.Net.Clients;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BingXOrderSide = BingX.Net.Enums.OrderSide;
using BingXOrderStatus = BingX.Net.Enums.OrderStatus;
using BingXOrderType = BingX.Net.Enums.OrderType;

namespace MyBot.Exchanges.BingX;

/// <summary>WebSocket client for the BingX cryptocurrency exchange using JK.BingX.Net SDK.</summary>
public class BingXWebSocketClient : IExchangeWebSocketClient
{
    private readonly BingXSocketClient _socketClient;
    private readonly BingXRestClient _restClient;
    private readonly ILogger<BingXWebSocketClient> _logger;
    private readonly Dictionary<string, UpdateSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _connected;
    private bool _disposed;

    /// <inheritdoc />
    public string ExchangeName => "BingX";

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
    /// Initializes a new <see cref="BingXWebSocketClient"/> with API credentials.
    /// </summary>
    public BingXWebSocketClient(string apiKey, string apiSecret, ILogger<BingXWebSocketClient> logger)
    {
        _logger = logger;
        var credentials = new ApiCredentials(apiKey, apiSecret);
        _socketClient = new BingXSocketClient(options => { options.ApiCredentials = credentials; });
        _restClient = new BingXRestClient(options => { options.ApiCredentials = credentials; });
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
        var result = await _socketClient.SpotApi.SubscribeToTickerUpdatesAsync(symbol, data =>
        {
            var t = data.Data;
            decimal changePercent = 0;
            if (decimal.TryParse(t.PriceChangePercentage?.TrimEnd('%'), out var pct))
                changePercent = pct;

            OnTickerUpdate?.Invoke(this, new WebSocketTickerUpdate
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
                Symbol = book.Symbol ?? symbol,
                Bids = book.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = book.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = book.Timestamp ?? DateTime.UtcNow
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
            var t = data.Data;
            OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
            {
                TradeId = t.TradeId.ToString(),
                Symbol = t.Symbol,
                Price = t.Price,
                Quantity = t.Quantity,
                TakerSide = t.BuyerIsMaker ? OrderSide.Sell : OrderSide.Buy,
                Exchange = ExchangeName,
                Timestamp = t.TradeTime
            });
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
                OrderId = o.OrderId.ToString(),
                ClientOrderId = o.ClientOrderId,
                Symbol = o.Symbol,
                Side = o.Side == BingXOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                Type = o.Type == BingXOrderType.Market ? OrderType.Market : OrderType.Limit,
                Status = MapOrderStatus(o.Status),
                Quantity = o.Quantity ?? 0,
                QuantityFilled = o.QuantityFilled ?? 0,
                Price = o.Price,
                Exchange = ExchangeName,
                Timestamp = o.UpdateTime ?? o.CreateTime
            });
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToUserBalanceAsync(CancellationToken cancellationToken = default)
    {
        const string key = "balance";
        var listenKey = await GetListenKeyAsync(cancellationToken);

        var result = await _socketClient.SpotApi.SubscribeToBalanceUpdatesAsync(listenKey, data =>
        {
            var balances = data.Data.EventData?.Balances ?? [];
            foreach (var b in balances)
            {
                OnBalanceUpdate?.Invoke(this, new WebSocketBalanceUpdate
                {
                    Asset = b.Asset,
                    Available = b.NewBalance - b.Locked,
                    Locked = b.Locked,
                    Total = b.NewBalance,
                    Exchange = ExchangeName,
                    Timestamp = data.Data.UpdateTime
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

    private static OrderStatus MapOrderStatus(BingXOrderStatus status) => status switch
    {
        BingXOrderStatus.New => OrderStatus.New,
        BingXOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        BingXOrderStatus.Filled => OrderStatus.Filled,
        BingXOrderStatus.Canceled => OrderStatus.Canceled,
        BingXOrderStatus.Failed => OrderStatus.Rejected,
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
