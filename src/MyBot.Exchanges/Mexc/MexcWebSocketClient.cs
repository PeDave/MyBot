using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets.Default;
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
    private readonly MexcRestClient? _restClient;
    private readonly ILogger<MexcWebSocketClient> _logger;
    private readonly Dictionary<string, UpdateSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string ExchangeName => "MEXC";

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

    /// <summary>Creates a MEXC WebSocket client for public streams (no authentication required).</summary>
    public MexcWebSocketClient(ILogger<MexcWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new MexcSocketClient();
    }

    /// <summary>Creates a MEXC WebSocket client with authentication for private streams.</summary>
    public MexcWebSocketClient(string apiKey, string apiSecret, ILogger<MexcWebSocketClient> logger)
    {
        _logger = logger;
        _socketClient = new MexcSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
        _restClient = new MexcRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // The MEXC SDK connects automatically on first subscription.
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
            var result = await _socketClient.SpotApi.SubscribeToMiniTickerUpdatesAsync(
                symbol,
                data =>
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
                        Symbol = symbol,
                        Bids = ob.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                        Asks = ob.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
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
                    foreach (var t in data.Data)
                    {
                        OnTradeUpdate?.Invoke(this, new WebSocketTradeUpdate
                        {
                            TradeId = string.Empty,
                            Symbol = symbol,
                            Price = t.Price,
                            Quantity = t.Quantity,
                            QuoteQuantity = t.Price * t.Quantity,
                            TakerSide = t.Side == MexcOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
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
                        OrderId = o.OrderId ?? string.Empty,
                        ClientOrderId = o.ClientOrderId,
                        // MEXC user order updates do not include the trading symbol
                        Symbol = string.Empty,
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
            var result = await _socketClient.SpotApi.SubscribeToAccountUpdatesAsync(
                listenKey,
                data =>
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

    private static OrderStatus MapOrderStatus(MexcOrderStatus status) => status switch
    {
        MexcOrderStatus.New => OrderStatus.New,
        MexcOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        MexcOrderStatus.Filled => OrderStatus.Filled,
        MexcOrderStatus.Canceled => OrderStatus.Canceled,
        MexcOrderStatus.PartiallyCanceled => OrderStatus.Canceled,
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
