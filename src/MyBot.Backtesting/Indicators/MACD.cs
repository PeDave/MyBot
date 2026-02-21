namespace MyBot.Backtesting.Indicators;

/// <summary>Moving Average Convergence Divergence (MACD) indicator.</summary>
public static class MACD
{
    /// <summary>
    /// Calculates MACD, Signal, and Histogram lines.
    /// Returns null values where there is insufficient data.
    /// </summary>
    public static (List<decimal?> macd, List<decimal?> signal, List<decimal?> histogram)
        Calculate(List<decimal> closePrices, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        var fastEma = EMA.Calculate(closePrices, fastPeriod);
        var slowEma = EMA.Calculate(closePrices, slowPeriod);

        var macdLine = new List<decimal?>(closePrices.Count);
        for (var i = 0; i < closePrices.Count; i++)
        {
            if (fastEma[i].HasValue && slowEma[i].HasValue)
                macdLine.Add(fastEma[i]!.Value - slowEma[i]!.Value);
            else
                macdLine.Add(null);
        }

        // Calculate signal line (EMA of MACD)
        var macdValues = macdLine.Select(v => v ?? 0m).ToList();
        var firstValid = macdLine.FindIndex(v => v.HasValue);
        var signalLine = new List<decimal?>(closePrices.Count);

        if (firstValid >= 0)
        {
            var validMacd = macdValues.Skip(firstValid).ToList();
            var signalEma = EMA.Calculate(validMacd, signalPeriod);

            for (var i = 0; i < firstValid; i++) signalLine.Add(null);
            for (var i = 0; i < signalEma.Count; i++) signalLine.Add(signalEma[i]);
        }
        else
        {
            for (var i = 0; i < closePrices.Count; i++) signalLine.Add(null);
        }

        var histogram = new List<decimal?>(closePrices.Count);
        for (var i = 0; i < closePrices.Count; i++)
        {
            if (macdLine[i].HasValue && signalLine[i].HasValue)
                histogram.Add(macdLine[i]!.Value - signalLine[i]!.Value);
            else
                histogram.Add(null);
        }

        return (macdLine, signalLine, histogram);
    }
}
