using MyBot.Core.Models;

namespace MyBot.Core.Interfaces;

/// <summary>
/// Defines the standard WebSocket operations available across all supported cryptocurrency exchanges.
/// Implementations manage subscriptions and normalize exchange-specific messages into unified models.
/// </summary>
public interface IExchangeWebSocketClient : IDisposable
{
    /// <summary>Gets the name of the exchange.</summary>
    string ExchangeName { get; }

    /// <summary>Gets a value indicating whether the client has active subscriptions.</summary>
    bool IsConnected { get; }

    /// <summary>Fired when a ticker/price update is received for a subscribed symbol.</summary>
    event EventHandler<WebSocketTickerUpdate> OnTickerUpdate;

    /// <summary>Fired when an order book update is received for a subscribed symbol.</summary>
    event EventHandler<WebSocketOrderBookUpdate> OnOrderBookUpdate;

    /// <summary>Fired when a trade execution update is received for a subscribed symbol.</summary>
    event EventHandler<WebSocketTradeUpdate> OnTradeUpdate;

    /// <summary>Fired when the authenticated user's order status changes.</summary>
    event EventHandler<WebSocketOrderUpdate> OnOrderUpdate;

    /// <summary>Fired when the authenticated user's balance changes.</summary>
    event EventHandler<WebSocketBalanceUpdate> OnBalanceUpdate;

    /// <summary>Fired when a WebSocket error occurs.</summary>
    event EventHandler<string> OnError;

    /// <summary>
    /// Initializes the client. This is a lightweight operation; actual connections are established
    /// on first subscription.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes all active subscriptions and disconnects from the exchange WebSocket.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>Subscribes to real-time ticker/price updates for the given symbol.</summary>
    /// <param name="symbol">Trading symbol, e.g. "BTCUSDT".</param>
    Task SubscribeToTickerAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to real-time order book updates for the given symbol.</summary>
    /// <param name="symbol">Trading symbol, e.g. "BTCUSDT".</param>
    /// <param name="depth">Number of order book levels to track.</param>
    Task SubscribeToOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to real-time trade execution updates for the given symbol.</summary>
    /// <param name="symbol">Trading symbol, e.g. "BTCUSDT".</param>
    Task SubscribeToTradesAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to the authenticated user's order status updates (requires API credentials).
    /// </summary>
    Task SubscribeToUserOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to the authenticated user's balance updates (requires API credentials).
    /// </summary>
    Task SubscribeToUserBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from the named stream and closes its underlying connection.
    /// Stream names follow the pattern: "ticker-SYMBOL", "orderbook-SYMBOL", "trades-SYMBOL",
    /// "orders", "balance".
    /// </summary>
    Task UnsubscribeAsync(string streamName);
}
