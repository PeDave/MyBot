using MyBot.Core.Models;

namespace MyBot.Core.Interfaces;

/// <summary>
/// Defines standard WebSocket operations available across all supported cryptocurrency exchanges.
/// </summary>
public interface IExchangeWebSocketClient : IDisposable
{
    /// <summary>Gets the name of the exchange.</summary>
    string ExchangeName { get; }

    /// <summary>Gets whether the WebSocket is currently connected.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when a ticker/price update is received.</summary>
    event EventHandler<WebSocketTickerUpdate>? OnTickerUpdate;

    /// <summary>Raised when an order book update is received.</summary>
    event EventHandler<WebSocketOrderBookUpdate>? OnOrderBookUpdate;

    /// <summary>Raised when a trade execution update is received.</summary>
    event EventHandler<WebSocketTradeUpdate>? OnTradeUpdate;

    /// <summary>Raised when a user order status update is received.</summary>
    event EventHandler<WebSocketOrderUpdate>? OnOrderUpdate;

    /// <summary>Raised when a user balance update is received.</summary>
    event EventHandler<WebSocketBalanceUpdate>? OnBalanceUpdate;

    /// <summary>Raised when a WebSocket error occurs.</summary>
    event EventHandler<string>? OnError;

    /// <summary>
    /// Establishes the WebSocket connection.
    /// Note: The underlying SDKs connect automatically on first subscription;
    /// this method may be used for explicit connection validation.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes all active subscriptions and disconnects.</summary>
    Task DisconnectAsync();

    /// <summary>Subscribes to real-time ticker/price updates for a symbol.</summary>
    Task SubscribeToTickerAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to real-time order book updates for a symbol.</summary>
    Task SubscribeToOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to real-time trade execution updates for a symbol.</summary>
    Task SubscribeToTradesAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to the authenticated user's order updates (requires API credentials).</summary>
    Task SubscribeToUserOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>Subscribes to the authenticated user's balance updates (requires API credentials).</summary>
    Task SubscribeToUserBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Unsubscribes from a specific stream by name.</summary>
    Task UnsubscribeAsync(string streamName, CancellationToken cancellationToken = default);
}
