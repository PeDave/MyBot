namespace MyBot.Backtesting.Indicators;

/// <summary>Relative Strength Index (RSI) indicator.</summary>
public static class RSI
{
    /// <summary>
    /// Calculates RSI over a list of closing prices.
    /// Returns null for positions where there is insufficient data.
    /// </summary>
    public static List<decimal?> Calculate(List<decimal> closePrices, int period = 14)
    {
        if (period <= 0) throw new ArgumentException("Period must be positive.", nameof(period));
        var result = new List<decimal?>(closePrices.Count);

        if (closePrices.Count <= period)
        {
            for (var i = 0; i < closePrices.Count; i++) result.Add(null);
            return result;
        }

        // Calculate initial average gains/losses (Wilder's method)
        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (var i = 1; i < closePrices.Count; i++)
        {
            var change = closePrices[i] - closePrices[i - 1];
            gains.Add(change > 0 ? change : 0m);
            losses.Add(change < 0 ? Math.Abs(change) : 0m);
        }

        // First RSI uses simple averages for the seed
        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        for (var i = 0; i <= period; i++) result.Add(null);

        // Wilder's smoothing from period onwards
        for (var i = period; i < gains.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;

            var rs = avgLoss == 0 ? 100m : avgGain / avgLoss;
            result.Add(100m - (100m / (1m + rs)));
        }

        return result;
    }
}
