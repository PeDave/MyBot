namespace MyBot.Backtesting.Indicators;

/// <summary>Simple Moving Average indicator.</summary>
public static class SMA
{
    /// <summary>
    /// Calculates the Simple Moving Average over a list of values.
    /// Returns null for positions where there is insufficient data (i &lt; period - 1).
    /// </summary>
    public static List<decimal?> Calculate(List<decimal> values, int period)
    {
        if (period <= 0) throw new ArgumentException("Period must be positive.", nameof(period));
        var result = new List<decimal?>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            if (i < period - 1)
            {
                result.Add(null);
            }
            else
            {
                var sum = 0m;
                for (var j = i - period + 1; j <= i; j++)
                    sum += values[j];
                result.Add(sum / period);
            }
        }
        return result;
    }
}
