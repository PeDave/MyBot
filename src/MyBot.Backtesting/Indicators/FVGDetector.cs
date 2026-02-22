using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Indicators;

/// <summary>
/// Represents a Fair Value Gap (FVG) - an imbalance in price caused by institutional orders.
/// </summary>
public class FairValueGap
{
    /// <summary>Index of the middle candle in the 3-candle FVG pattern.</summary>
    public int Index { get; set; }
    /// <summary>Timestamp of the middle candle.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Top of the gap.</summary>
    public decimal GapTop { get; set; }
    /// <summary>Bottom of the gap.</summary>
    public decimal GapBottom { get; set; }
    /// <summary>Size of the gap (GapTop - GapBottom).</summary>
    public decimal GapSize => GapTop - GapBottom;
    /// <summary>True = bullish FVG (institutional buying pressure); false = bearish.</summary>
    public bool IsBullish { get; set; }
    /// <summary>Whether the gap has been at least 50% filled by subsequent price action.</summary>
    public bool IsFilled { get; set; }
    /// <summary>Fraction of the gap that has been filled (0–1).</summary>
    public decimal PercentFilled { get; set; } = 0;
    /// <summary>Midpoint of the gap, used as a reference entry price.</summary>
    public decimal MidPoint => (GapTop + GapBottom) / 2;

    /// <summary>
    /// Updates fill status based on the current candle's price range.
    /// The gap is considered filled once 50% or more of its range has been covered.
    /// </summary>
    public void UpdateFillStatus(decimal currentLow, decimal currentHigh)
    {
        if (GapSize <= 0) return;

        if (IsBullish)
        {
            // Bullish FVG is filled when price drops into the gap.
            // Clamp fill level to GapBottom so PercentFilled stays in [0, 1].
            if (currentLow <= GapTop)
            {
                var fillLevel = Math.Max(currentLow, GapBottom);
                PercentFilled = (GapTop - fillLevel) / GapSize;
                IsFilled = PercentFilled >= 0.5m;
            }
        }
        else
        {
            // Bearish FVG is filled when price rises into the gap.
            // Clamp fill level to GapTop so PercentFilled stays in [0, 1].
            if (currentHigh >= GapBottom)
            {
                var fillLevel = Math.Min(currentHigh, GapTop);
                PercentFilled = (fillLevel - GapBottom) / GapSize;
                IsFilled = PercentFilled >= 0.5m;
            }
        }
    }
}

/// <summary>
/// Detects Fair Value Gaps (FVG) in OHLCV candle data.
/// </summary>
public static class FVGDetector
{
    /// <summary>
    /// Detects all Fair Value Gaps in the candle list.
    /// A bullish FVG exists when <c>candles[i-1].High &lt; candles[i+1].Low</c>;
    /// a bearish FVG exists when <c>candles[i-1].Low &gt; candles[i+1].High</c>.
    /// </summary>
    /// <param name="candles">Ordered list of OHLCV candles.</param>
    /// <param name="minGapPercent">Minimum gap size as a percentage of the previous close (default 0).</param>
    public static List<FairValueGap> DetectFVGs(List<OHLCVCandle> candles, decimal minGapPercent = 0)
    {
        var fvgs = new List<FairValueGap>();

        for (int i = 1; i < candles.Count - 1; i++)
        {
            var prev = candles[i - 1];
            var current = candles[i];
            var next = candles[i + 1];

            // Bullish FVG: gap between previous high and next low
            if (prev.High < next.Low)
            {
                var gapSize = next.Low - prev.High;
                var gapPercent = prev.Close > 0 ? (gapSize / prev.Close) * 100 : 0;

                if (gapPercent >= minGapPercent)
                {
                    fvgs.Add(new FairValueGap
                    {
                        Index = i,
                        Timestamp = current.Timestamp,
                        GapTop = next.Low,
                        GapBottom = prev.High,
                        IsBullish = true
                    });
                }
            }

            // Bearish FVG: gap between previous low and next high
            if (prev.Low > next.High)
            {
                var gapSize = prev.Low - next.High;
                var gapPercent = prev.Close > 0 ? (gapSize / prev.Close) * 100 : 0;

                if (gapPercent >= minGapPercent)
                {
                    fvgs.Add(new FairValueGap
                    {
                        Index = i,
                        Timestamp = current.Timestamp,
                        GapTop = prev.Low,
                        GapBottom = next.High,
                        IsBullish = false
                    });
                }
            }
        }

        return fvgs;
    }

    /// <summary>
    /// Updates fill status for all unfilled FVGs based on subsequent price action.
    /// </summary>
    public static void UpdateFVGFillStatus(List<FairValueGap> fvgs, List<OHLCVCandle> candles)
    {
        foreach (var fvg in fvgs.Where(f => !f.IsFilled))
        {
            for (int i = fvg.Index + 2; i < candles.Count; i++)
            {
                fvg.UpdateFillStatus(candles[i].Low, candles[i].High);
                if (fvg.IsFilled) break;
            }
        }
    }
}
