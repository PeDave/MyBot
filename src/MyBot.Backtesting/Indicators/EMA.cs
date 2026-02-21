namespace MyBot.Backtesting.Indicators;

/// <summary>Exponential Moving Average indicator.</summary>
public static class EMA
{
    /// <summary>
    /// Calculates the Exponential Moving Average over a list of values.
    /// Returns null for positions where there is insufficient data (i &lt; period - 1).
    /// </summary>
    public static List<decimal?> Calculate(List<decimal> values, int period)
    {
        if (period <= 0) throw new ArgumentException("Period must be positive.", nameof(period));
        var result = new List<decimal?>(values.Count);
        var multiplier = 2m / (period + 1m);
        decimal? ema = null;

        for (var i = 0; i < values.Count; i++)
        {
            if (i < period - 1)
            {
                result.Add(null);
            }
            else if (i == period - 1)
            {
                // Seed with SMA
                var sum = 0m;
                for (var j = 0; j < period; j++) sum += values[j];
                ema = sum / period;
                result.Add(ema);
            }
            else
            {
                ema = (values[i] - ema!.Value) * multiplier + ema.Value;
                result.Add(ema);
            }
        }
        return result;
    }
}
