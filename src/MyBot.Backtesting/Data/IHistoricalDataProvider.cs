using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Data;

/// <summary>Interface for fetching historical OHLCV candle data.</summary>
public interface IHistoricalDataProvider
{
    /// <summary>
    /// Fetches historical OHLCV candle data for a symbol from an exchange.
    /// </summary>
    /// <param name="exchange">Exchange name (e.g., "bitget", "bybit").</param>
    /// <param name="symbol">Trading symbol (e.g., "BTCUSDT").</param>
    /// <param name="startDate">Start date for historical data.</param>
    /// <param name="endDate">End date for historical data.</param>
    /// <param name="timeframe">Candle timeframe: "1m", "5m", "15m", "30m", "1h", "4h", "1d".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<OHLCVCandle>> GetHistoricalDataAsync(
        string exchange,
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string timeframe,
        CancellationToken cancellationToken = default);
}
