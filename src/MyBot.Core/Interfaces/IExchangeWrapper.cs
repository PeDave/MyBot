using MyBot.Core.Models;

namespace MyBot.Core.Interfaces;

/// <summary>
/// Defines the standard operations available across all supported cryptocurrency exchanges.
/// </summary>
public interface IExchangeWrapper
{
    /// <summary>Gets the name of the exchange.</summary>
    string ExchangeName { get; }

    /// <summary>Gets the account balances for the exchange.</summary>
    Task<IEnumerable<UnifiedBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>Places an order on the exchange.</summary>
    Task<UnifiedOrder> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null, TimeInForce timeInForce = TimeInForce.GoodTillCanceled, CancellationToken cancellationToken = default);

    /// <summary>Cancels an existing order.</summary>
    Task<UnifiedOrder> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default);

    /// <summary>Gets the status of a specific order.</summary>
    Task<UnifiedOrder> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default);

    /// <summary>Gets all currently open orders.</summary>
    Task<IEnumerable<UnifiedOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default);

    /// <summary>Gets order history.</summary>
    Task<IEnumerable<UnifiedOrder>> GetOrderHistoryAsync(string? symbol = null, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Gets the current ticker/price information for a symbol.</summary>
    Task<UnifiedTicker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Gets market data (order book) for a symbol.</summary>
    Task<UnifiedOrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default);

    /// <summary>Gets recent trades for a symbol.</summary>
    Task<IEnumerable<UnifiedTrade>> GetRecentTradesAsync(string symbol, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Gets historical OHLCV kline/candle data for a symbol.</summary>
    /// <param name="symbol">Trading symbol (e.g., "BTCUSDT").</param>
    /// <param name="timeframe">Timeframe: "1m", "5m", "15m", "30m", "1h", "4h", "1d".</param>
    /// <param name="startTime">Start time for historical data.</param>
    /// <param name="endTime">End time for historical data.</param>
    /// <param name="limit">Maximum number of candles to return (default 200, max varies by exchange).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IEnumerable<UnifiedKline>> GetKlinesAsync(string symbol, string timeframe, DateTime startTime, DateTime endTime, int limit = 200, CancellationToken cancellationToken = default);
}
