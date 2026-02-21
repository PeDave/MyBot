namespace MyBot.Backtesting.Indicators;

/// <summary>Donchian Channel indicator — highest high and lowest low over N periods.</summary>
public static class DonchianChannel
{
    /// <summary>
    /// Calculates Donchian Channels: upper (highest high), lower (lowest low),
    /// and middle ((upper + lower) / 2) over the given period.
    /// Returns null values where there is insufficient data.
    /// </summary>
    public static (List<decimal?> upper, List<decimal?> middle, List<decimal?> lower)
        Calculate(List<decimal> highs, List<decimal> lows, int period = 20)
    {
        if (period <= 0) throw new ArgumentException("Period must be positive.", nameof(period));
        if (highs.Count != lows.Count)
            throw new ArgumentException("Highs and lows must have the same length.");

        var count = highs.Count;
        var upper = new List<decimal?>(count);
        var middle = new List<decimal?>(count);
        var lower = new List<decimal?>(count);

        for (var i = 0; i < count; i++)
        {
            if (i < period - 1)
            {
                upper.Add(null);
                middle.Add(null);
                lower.Add(null);
                continue;
            }

            var highestHigh = highs[i];
            var lowestLow = lows[i];
            for (var j = i - period + 1; j <= i; j++)
            {
                if (highs[j] > highestHigh) highestHigh = highs[j];
                if (lows[j] < lowestLow) lowestLow = lows[j];
            }

            upper.Add(highestHigh);
            lower.Add(lowestLow);
            middle.Add((highestHigh + lowestLow) / 2m);
        }

        return (upper, middle, lower);
    }
}
