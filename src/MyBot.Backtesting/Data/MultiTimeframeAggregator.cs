using System.Globalization;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Data;

/// <summary>
/// Aggregates candles into higher timeframes (daily, weekly).
/// Useful for multi-timeframe strategy analysis.
/// </summary>
public static class MultiTimeframeAggregator
{
    /// <summary>
    /// Aggregates a list of candles into daily candles.
    /// If the input is already daily (one candle per day), the result is unchanged.
    /// </summary>
    /// <param name="candles">Input candles (any timeframe).</param>
    /// <returns>Daily OHLCV candles ordered by date.</returns>
    public static List<OHLCVCandle> ToDailyCandles(List<OHLCVCandle> candles)
    {
        return candles
            .GroupBy(c => c.Timestamp.Date)
            .OrderBy(g => g.Key)
            .Select(g => new OHLCVCandle
            {
                Timestamp = g.Key,
                Open      = g.First().Open,
                High      = g.Max(c => c.High),
                Low       = g.Min(c => c.Low),
                Close     = g.Last().Close,
                Volume    = g.Sum(c => c.Volume),
                Symbol    = g.First().Symbol,
                Exchange  = g.First().Exchange
            })
            .ToList();
    }

    /// <summary>
    /// Aggregates a list of candles into weekly candles (ISO week / Monday-based).
    /// </summary>
    /// <param name="candles">Input candles (any timeframe).</param>
    /// <returns>Weekly OHLCV candles ordered by ISO week.</returns>
    public static List<OHLCVCandle> ToWeeklyCandles(List<OHLCVCandle> candles)
    {
        return candles
            .GroupBy(c => GetIsoWeekKey(c.Timestamp))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var sorted = g.OrderBy(c => c.Timestamp).ToList();
                return new OHLCVCandle
                {
                    Timestamp = sorted.First().Timestamp,
                    Open      = sorted.First().Open,
                    High      = sorted.Max(c => c.High),
                    Low       = sorted.Min(c => c.Low),
                    Close     = sorted.Last().Close,
                    Volume    = sorted.Sum(c => c.Volume),
                    Symbol    = sorted.First().Symbol,
                    Exchange  = sorted.First().Exchange
                };
            })
            .ToList();
    }

    private static (int Year, int Week) GetIsoWeekKey(DateTime dt)
    {
        var week = ISOWeek.GetWeekOfYear(dt);
        var year = ISOWeek.GetYear(dt);
        return (year, week);
    }
}
