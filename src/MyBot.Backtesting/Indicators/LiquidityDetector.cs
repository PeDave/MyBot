using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Indicators;

/// <summary>
/// Represents a liquidity zone (swing high or swing low where stop-losses cluster).
/// </summary>
public class LiquidityZone
{
    /// <summary>Index of the swing point candle.</summary>
    public int Index { get; set; }
    /// <summary>Timestamp of the swing point.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Price level of the swing high or low.</summary>
    public decimal Price { get; set; }
    /// <summary>True = swing high (resistance); false = swing low (support).</summary>
    public bool IsHigh { get; set; }
    /// <summary>Combined left + right bar count used to qualify the swing.</summary>
    public int Strength { get; set; }
    /// <summary>Whether this level has been swept (price broke through it).</summary>
    public bool IsSwept { get; set; } = false;
    /// <summary>Timestamp when the sweep occurred.</summary>
    public DateTime? SweptAt { get; set; }
}

/// <summary>
/// Detects swing-high/swing-low liquidity zones and liquidity sweeps.
/// </summary>
public static class LiquidityDetector
{
    /// <summary>
    /// Detects swing highs and lows (liquidity zones) using a left/right bar confirmation window.
    /// </summary>
    /// <param name="candles">Ordered list of OHLCV candles.</param>
    /// <param name="leftBars">Number of bars to the left that must have lower highs / higher lows.</param>
    /// <param name="rightBars">Number of bars to the right that must have lower highs / higher lows.</param>
    public static List<LiquidityZone> DetectSwingPoints(
        List<OHLCVCandle> candles,
        int leftBars = 5,
        int rightBars = 5)
    {
        var zones = new List<LiquidityZone>();

        for (int i = leftBars; i < candles.Count - rightBars; i++)
        {
            var current = candles[i];

            // Check for swing high
            bool isSwingHigh = true;
            for (int j = 1; j <= leftBars && isSwingHigh; j++)
            {
                if (candles[i - j].High >= current.High)
                    isSwingHigh = false;
            }
            for (int j = 1; j <= rightBars && isSwingHigh; j++)
            {
                if (candles[i + j].High >= current.High)
                    isSwingHigh = false;
            }

            if (isSwingHigh)
            {
                zones.Add(new LiquidityZone
                {
                    Index = i,
                    Timestamp = current.Timestamp,
                    Price = current.High,
                    IsHigh = true,
                    Strength = leftBars + rightBars
                });
            }

            // Check for swing low
            bool isSwingLow = true;
            for (int j = 1; j <= leftBars && isSwingLow; j++)
            {
                if (candles[i - j].Low <= current.Low)
                    isSwingLow = false;
            }
            for (int j = 1; j <= rightBars && isSwingLow; j++)
            {
                if (candles[i + j].Low <= current.Low)
                    isSwingLow = false;
            }

            if (isSwingLow)
            {
                zones.Add(new LiquidityZone
                {
                    Index = i,
                    Timestamp = current.Timestamp,
                    Price = current.Low,
                    IsHigh = false,
                    Strength = leftBars + rightBars
                });
            }
        }

        return zones;
    }

    /// <summary>
    /// Marks liquidity zones as swept when subsequent price action breaks beyond the level.
    /// </summary>
    /// <param name="zones">List of previously detected liquidity zones.</param>
    /// <param name="candles">Ordered list of OHLCV candles.</param>
    /// <param name="sweepThreshold">Fractional threshold beyond the level required to count as a sweep (default 0.1%).</param>
    public static void DetectLiquiditySweeps(
        List<LiquidityZone> zones,
        List<OHLCVCandle> candles,
        decimal sweepThreshold = 0.001m)
    {
        foreach (var zone in zones.Where(z => !z.IsSwept))
        {
            for (int i = zone.Index + 1; i < candles.Count; i++)
            {
                var candle = candles[i];

                if (zone.IsHigh)
                {
                    if (candle.High > zone.Price * (1 + sweepThreshold))
                    {
                        zone.IsSwept = true;
                        zone.SweptAt = candle.Timestamp;
                        break;
                    }
                }
                else
                {
                    if (candle.Low < zone.Price * (1 - sweepThreshold))
                    {
                        zone.IsSwept = true;
                        zone.SweptAt = candle.Timestamp;
                        break;
                    }
                }
            }
        }
    }
}
