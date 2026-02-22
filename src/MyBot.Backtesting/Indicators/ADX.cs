namespace MyBot.Backtesting.Indicators;

/// <summary>Average Directional Index (ADX) indicator result for a single candle.</summary>
public class AdxValue
{
    /// <summary>Average Directional Index (0-100). Higher values indicate stronger trend.</summary>
    public decimal Adx { get; set; }
    /// <summary>Positive Directional Indicator (+DI).</summary>
    public decimal PlusDI { get; set; }
    /// <summary>Negative Directional Indicator (-DI).</summary>
    public decimal MinusDI { get; set; }
}

/// <summary>
/// Average Directional Index (ADX) indicator for measuring trend strength.
/// ADX &gt; 25 indicates a trending market; ADX &lt; 20 indicates a ranging market.
/// </summary>
public static class ADX
{
    /// <summary>
    /// Calculates ADX, +DI and -DI values for the given OHLC data.
    /// Returns null for positions where there is insufficient data.
    /// </summary>
    /// <param name="highs">High prices.</param>
    /// <param name="lows">Low prices.</param>
    /// <param name="closes">Close prices.</param>
    /// <param name="period">Smoothing period (default 14).</param>
    public static List<AdxValue?> Calculate(
        List<decimal> highs,
        List<decimal> lows,
        List<decimal> closes,
        int period = 14)
    {
        if (period <= 0) throw new ArgumentException("Period must be positive.", nameof(period));
        if (highs.Count != lows.Count || highs.Count != closes.Count)
            throw new ArgumentException("Highs, lows, and closes must have the same length.");

        var count = highs.Count;
        var result = new List<AdxValue?>(count);

        if (count < period * 2)
        {
            for (var i = 0; i < count; i++) result.Add(null);
            return result;
        }

        // Step 1: Calculate True Range and Directional Movement
        var trList = new decimal[count];
        var plusDMList = new decimal[count];
        var minusDMList = new decimal[count];

        for (var i = 1; i < count; i++)
        {
            var highDiff = highs[i] - highs[i - 1];
            var lowDiff = lows[i - 1] - lows[i];

            plusDMList[i] = highDiff > lowDiff && highDiff > 0 ? highDiff : 0m;
            minusDMList[i] = lowDiff > highDiff && lowDiff > 0 ? lowDiff : 0m;

            var hl = highs[i] - lows[i];
            var hpc = Math.Abs(highs[i] - closes[i - 1]);
            var lpc = Math.Abs(lows[i] - closes[i - 1]);
            trList[i] = Math.Max(hl, Math.Max(hpc, lpc));
        }

        // Step 2: Wilder's smoothing (initial sum then rolling)
        var smoothTr = trList.Skip(1).Take(period).Sum();
        var smoothPlusDM = plusDMList.Skip(1).Take(period).Sum();
        var smoothMinusDM = minusDMList.Skip(1).Take(period).Sum();

        // Placeholders for indices 0..period
        for (var i = 0; i <= period; i++) result.Add(null);

        var dxList = new List<decimal>();

        for (var i = period + 1; i < count; i++)
        {
            smoothTr = smoothTr - (smoothTr / period) + trList[i];
            smoothPlusDM = smoothPlusDM - (smoothPlusDM / period) + plusDMList[i];
            smoothMinusDM = smoothMinusDM - (smoothMinusDM / period) + minusDMList[i];

            var plusDI = smoothTr > 0 ? (smoothPlusDM / smoothTr) * 100m : 0m;
            var minusDI = smoothTr > 0 ? (smoothMinusDM / smoothTr) * 100m : 0m;

            var diSum = plusDI + minusDI;
            var dx = diSum > 0 ? Math.Abs(plusDI - minusDI) / diSum * 100m : 0m;
            dxList.Add(dx);

            // We need at least `period` DX values to compute the first ADX
            if (dxList.Count < period)
            {
                result.Add(null);
            }
            else if (dxList.Count == period)
            {
                var adx = dxList.Average();
                result.Add(new AdxValue { Adx = adx, PlusDI = plusDI, MinusDI = minusDI });
            }
            else
            {
                var prevAdx = result[^1]!.Adx;
                var adx = (prevAdx * (period - 1) + dx) / period;
                result.Add(new AdxValue { Adx = adx, PlusDI = plusDI, MinusDI = minusDI });
            }
        }

        return result;
    }
}
