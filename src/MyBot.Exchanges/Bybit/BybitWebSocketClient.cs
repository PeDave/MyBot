using Bybit.Net.Clients;
using Bybit.Net.SymbolOrderBooks;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BybitCategory = Bybit.Net.Enums.Category;
using BybitOrderSide = Bybit.Net.Enums.OrderSide;
using BybitOrderStatus = Bybit.Net.Enums.OrderStatus;
using BybitOrderType = Bybit.Net.Enums.OrderType;

namespace MyBot.Exchanges.Bybit;

/// <summary>WebSocket client for the Bybit cryptocurrency exchange using Bybit.Net SDK.</summary>
public class BybitWebSocketClient : IExchangeWebSocketClient
{
    private readonly BybitSocketClient _socketClient;
    private readonly ILogger<BybitWebSocketClient> _logger;
    private readonly Dictionary<string, UpdateSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BybitSymbolOrderBook> _orderBooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _connected;
    private bool _disposed;

    /// <inheritdoc />
    public string ExchangeName => "Bybit";

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
    /// Initializes a new <see cref="BybitWebSocketClient"/> with API credentials.
    /// </summary>
    public BybitWebSocketClient(string apiKey, string apiSecret, ILogger<BybitWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BybitSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
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
            foreach (var ob in _orderBooks.Values)
                await ob.StopAsync();
            _orderBooks.Clear();
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
        var result = await _socketClient.V5SpotApi.SubscribeToTickerUpdatesAsync(symbol, data =>
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
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        var key = "orderbook-" + symbol;

        // Bybit V5SpotApi does not expose a direct order book subscription method;
        // use BybitSymbolOrderBook which manages the WebSocket connection internally.
        var orderBook = new BybitSymbolOrderBook(symbol, BybitCategory.Spot,
            options => { options.Limit = depth; },
            NullLoggerFactory.Instance,
            _socketClient);

        orderBook.OnOrderBookUpdate += update =>
        {
            OnOrderBookUpdate?.Invoke(this, new WebSocketOrderBookUpdate
            {
                Symbol = symbol,
                Bids = update.Item1.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = update.Item2.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = orderBook.UpdateTime
            });
        };

        var startResult = await orderBook.StartAsync(cancellationToken);
        if (!startResult.Success)
        {
            var msg = startResult.Error?.Message ?? "Order book subscription failed";
            _logger.LogError("Failed to subscribe to {Stream} on {Exchange}: {Error}", key, ExchangeName, msg);
            OnError?.Invoke(this, $"{key}: {msg}");
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, key, msg);
        }

        // Wrap the order book lifecycle in a pseudo UpdateSubscription adapter
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _orderBooks[key] = orderBook;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SubscribeToTradesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var key = "trades-" + symbol;
        var result = await _socketClient.V5SpotApi.SubscribeToTradeUpdatesAsync(symbol, data =>
        {
            foreach (var t in data.Data)
            {
                OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
                {
                    TradeId = t.TradeId,
                    Symbol = t.Symbol,
                    Price = t.Price,
                    Quantity = t.Quantity,
                    TakerSide = t.Side == BybitOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
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
        var result = await _socketClient.V5PrivateApi.SubscribeToOrderUpdatesAsync(data =>
        {
            foreach (var o in data.Data)
            {
                OnOrderUpdate?.Invoke(this, new WebSocketOrderUpdate
                {
                    OrderId = o.OrderId,
                    ClientOrderId = o.ClientOrderId,
                    Symbol = o.Symbol,
                    Side = o.Side == BybitOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                    Type = o.OrderType == BybitOrderType.Market ? OrderType.Market : OrderType.Limit,
                    Status = MapOrderStatus(o.Status),
                    Quantity = o.Quantity,
                    QuantityFilled = o.QuantityFilled ?? 0,
                    Price = o.Price,
                    AveragePrice = o.AveragePrice,
                    Exchange = ExchangeName,
                    Timestamp = o.UpdateTime
                });
            }
        }, cancellationToken);

        await StoreSubscriptionAsync(key, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeToUserBalanceAsync(CancellationToken cancellationToken = default)
    {
        const string key = "balance";
        var result = await _socketClient.V5PrivateApi.SubscribeToWalletUpdatesAsync(data =>
        {
            foreach (var account in data.Data)
            {
                foreach (var asset in account.Assets)
                {
                    OnBalanceUpdate?.Invoke(this, new WebSocketBalanceUpdate
                    {
                        Asset = asset.Asset,
                        Available = asset.Free ?? 0,
                        Locked = asset.Locked ?? 0,
                        Total = (asset.Free ?? 0) + (asset.Locked ?? 0),
                        Exchange = ExchangeName,
                        Timestamp = DateTime.UtcNow
                    });
                }
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
            if (_orderBooks.TryGetValue(streamName, out var ob))
            {
                await ob.StopAsync();
                _orderBooks.Remove(streamName);
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

    private static MyBot.Core.Models.OrderStatus MapOrderStatus(BybitOrderStatus status) => status switch
    {
        BybitOrderStatus.New => MyBot.Core.Models.OrderStatus.New,
        BybitOrderStatus.PartiallyFilled => MyBot.Core.Models.OrderStatus.PartiallyFilled,
        BybitOrderStatus.Filled => MyBot.Core.Models.OrderStatus.Filled,
        BybitOrderStatus.Cancelled => MyBot.Core.Models.OrderStatus.Canceled,
        BybitOrderStatus.Rejected => MyBot.Core.Models.OrderStatus.Rejected,
        BybitOrderStatus.PartiallyFilledCanceled => MyBot.Core.Models.OrderStatus.Canceled,
        _ => MyBot.Core.Models.OrderStatus.Unknown
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
