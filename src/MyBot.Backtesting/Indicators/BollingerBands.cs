namespace MyBot.Backtesting.Indicators;

/// <summary>Bollinger Bands indicator.</summary>
public static class BollingerBands
{
    /// <summary>
    /// Calculates Bollinger Bands (upper, middle/SMA, lower).
    /// Returns null values where there is insufficient data.
    /// </summary>
    public static (List<decimal?> upper, List<decimal?> middle, List<decimal?> lower)
        Calculate(List<decimal> closePrices, int period = 20, decimal stdDevMultiplier = 2m)
    {
        var middle = SMA.Calculate(closePrices, period);
        var upper = new List<decimal?>(closePrices.Count);
        var lower = new List<decimal?>(closePrices.Count);

        for (var i = 0; i < closePrices.Count; i++)
        {
            if (!middle[i].HasValue)
            {
                upper.Add(null);
                lower.Add(null);
                continue;
            }

            var sma = middle[i]!.Value;
            var variance = 0m;
            for (var j = i - period + 1; j <= i; j++)
                variance += (closePrices[j] - sma) * (closePrices[j] - sma);
            var stdDev = (decimal)Math.Sqrt((double)(variance / period));

            upper.Add(sma + stdDevMultiplier * stdDev);
            lower.Add(sma - stdDevMultiplier * stdDev);
        }

        return (upper, middle, lower);
    }
}
