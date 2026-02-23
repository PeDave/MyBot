using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Data;

/// <summary>
/// Aggregates candles into higher timeframes (daily, weekly).
/// Delegates to <see cref="MultiTimeframeAggregator"/> for the actual aggregation logic.
/// </summary>
public static class TimeframeAggregator
{
    /// <summary>Aggregates input candles into daily OHLCV candles.</summary>
    public static List<OHLCVCandle> AggregateToDaily(List<OHLCVCandle> candles)
        => MultiTimeframeAggregator.ToDailyCandles(candles);

    /// <summary>Aggregates daily candles into weekly OHLCV candles (ISO week / Monday-based).</summary>
    public static List<OHLCVCandle> AggregateToWeekly(List<OHLCVCandle> dailyCandles)
        => MultiTimeframeAggregator.ToWeeklyCandles(dailyCandles);
}
