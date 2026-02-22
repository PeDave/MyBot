namespace MyBot.Backtesting.Indicators;

/// <summary>Average True Range (ATR) indicator for volatility measurement.</summary>
public static class ATR
{
    /// <summary>
    /// Calculates Average True Range (ATR) using an EMA of the True Range.
    /// True Range = max(high-low, |high-prevClose|, |low-prevClose|).
    /// Returns null for positions where there is insufficient data.
    /// </summary>
    public static List<decimal?> Calculate(
        List<decimal> highs,
        List<decimal> lows,
        List<decimal> closes,
        int period = 14)
    {
        if (period <= 0) throw new ArgumentException("Period must be positive.", nameof(period));
        if (highs.Count != lows.Count || highs.Count != closes.Count)
            throw new ArgumentException("Highs, lows, and closes must have the same length.");

        var count = highs.Count;
        var trueRanges = new List<decimal>(count);

        for (var i = 0; i < count; i++)
        {
            if (i == 0)
            {
                trueRanges.Add(highs[i] - lows[i]);
            }
            else
            {
                var hl = highs[i] - lows[i];
                var hpc = Math.Abs(highs[i] - closes[i - 1]);
                var lpc = Math.Abs(lows[i] - closes[i - 1]);
                trueRanges.Add(Math.Max(hl, Math.Max(hpc, lpc)));
            }
        }

        // ATR = EMA of True Range
        return EMA.Calculate(trueRanges, period);
    }
}
