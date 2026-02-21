using BingX.Net.Clients;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets.Default;
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
    private readonly BingXRestClient? _restClient;
    private readonly ILogger<BingXWebSocketClient> _logger;
    private readonly Dictionary<string, UpdateSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string ExchangeName => "BingX";

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

    /// <summary>Creates a BingX WebSocket client for public streams (no authentication required).</summary>
    public BingXWebSocketClient(ILogger<BingXWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BingXSocketClient();
    }

    /// <summary>Creates a BingX WebSocket client with authentication for private streams.</summary>
    public BingXWebSocketClient(string apiKey, string apiSecret, ILogger<BingXWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new BingXSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
        _restClient = new BingXRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // The BingX SDK connects automatically on first subscription.
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
            var result = await _socketClient.SpotApi.SubscribeToTickerUpdatesAsync(
                symbol,
                data =>
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
                        Timestamp = t.EventTime
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
            var result = await _socketClient.SpotApi.SubscribeToPartialOrderBookUpdatesAsync(
                symbol,
                depth,
                data =>
                {
                    var ob = data.Data;
                    OnOrderBookUpdate?.Invoke(this, new WebSocketOrderBookUpdate
                    {
                        Symbol = ob.Symbol ?? symbol,
                        Bids = ob.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                        Asks = ob.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                        Exchange = ExchangeName,
                        Timestamp = ob.Timestamp ?? DateTime.UtcNow
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
            var result = await _socketClient.SpotApi.SubscribeToTradeUpdatesAsync(
                symbol,
                data =>
                {
                    var t = data.Data;
                    OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
                    {
                        TradeId = t.TradeId.ToString(),
                        Symbol = t.Symbol,
                        Price = t.Price,
                        Quantity = t.Quantity,
                        QuoteQuantity = t.Price * t.Quantity,
                        TakerSide = t.BuyerIsMaker ? OrderSide.Sell : OrderSide.Buy,
                        Exchange = ExchangeName,
                        Timestamp = t.TradeTime
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
            _logger.LogError(ex, "Error subscribing to trades on {Exchange} for {Symbol}", ExchangeName, symbol);
            OnError?.Invoke(this, ex.Message);
            throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribeToUserOrdersAsync(CancellationToken cancellationToken = default)
    {
        const string streamName = "user_orders";
        if (_restClient is null)
            throw new ExchangeWebSocketException(ExchangeName, "API credentials required for private streams.", streamName);

        try
        {
            var listenKeyResult = await _restClient.SpotApi.Account.StartUserStreamAsync(cancellationToken);
            if (!listenKeyResult.Success)
                throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, listenKeyResult.Error?.Message ?? "Failed to get listen key");

            var listenKey = listenKeyResult.Data;
            var result = await _socketClient.SpotApi.SubscribeToOrderUpdatesAsync(
                listenKey,
                data =>
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
        if (_restClient is null)
            throw new ExchangeWebSocketException(ExchangeName, "API credentials required for private streams.", streamName);

        try
        {
            var listenKeyResult = await _restClient.SpotApi.Account.StartUserStreamAsync(cancellationToken);
            if (!listenKeyResult.Success)
                throw new ExchangeWebSocketSubscriptionException(ExchangeName, streamName, listenKeyResult.Error?.Message ?? "Failed to get listen key");

            var listenKey = listenKeyResult.Data;
            var result = await _socketClient.SpotApi.SubscribeToBalanceUpdatesAsync(
                listenKey,
                data =>
                {
                    var update = data.Data;
                    if (update.EventData?.Balances is null) return;
                    foreach (var b in update.EventData.Balances)
                    {
                        OnBalanceUpdate?.Invoke(this, new WebSocketBalanceUpdate
                        {
                            Asset = b.Asset,
                            Available = b.NewBalance - b.Locked,
                            Locked = b.Locked,
                            Total = b.NewBalance,
                            Exchange = ExchangeName,
                            Timestamp = update.UpdateTime
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
                await existing.CloseAsync();
            _subscriptions[streamName] = subscription;
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

    /// <inheritdoc/>
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _socketClient.Dispose();
        _restClient?.Dispose();
        _lock.Dispose();
    }
}
