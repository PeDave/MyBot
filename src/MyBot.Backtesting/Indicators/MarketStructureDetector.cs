using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Indicators;

/// <summary>Type of market structure break detected.</summary>
public enum StructureBreak
{
    /// <summary>No structure break.</summary>
    None,
    /// <summary>Break of Structure upward (trend continuation in uptrend).</summary>
    BullishBOS,
    /// <summary>Break of Structure downward (trend continuation in downtrend).</summary>
    BearishBOS,
    /// <summary>Change of Character upward (reversal from downtrend to uptrend).</summary>
    BullishCHOCH,
    /// <summary>Change of Character downward (reversal from uptrend to downtrend).</summary>
    BearishCHOCH
}

/// <summary>Represents a single market structure break event.</summary>
public class MarketStructure
{
    /// <summary>Index of the candle where the break occurred.</summary>
    public int Index { get; set; }
    /// <summary>Timestamp of the break candle.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Type of structure break.</summary>
    public StructureBreak Type { get; set; }
    /// <summary>Price level that was broken.</summary>
    public decimal BreakLevel { get; set; }
}

/// <summary>
/// Detects Break of Structure (BOS) and Change of Character (CHOCH) events.
/// </summary>
public static class MarketStructureDetector
{
    /// <summary>
    /// Detects BOS and CHOCH events using swing highs/lows as reference points.
    /// A BOS occurs when price breaks a prior swing in the direction of the current trend.
    /// A CHOCH occurs when price breaks a prior swing against the current trend (reversal signal).
    /// </summary>
    /// <param name="candles">Ordered list of OHLCV candles.</param>
    /// <param name="swingPoints">Swing highs and lows detected by <see cref="LiquidityDetector.DetectSwingPoints"/>.</param>
    public static List<MarketStructure> DetectStructureBreaks(
        List<OHLCVCandle> candles,
        List<LiquidityZone> swingPoints)
    {
        var structures = new List<MarketStructure>();
        var trend = DetermineTrend(swingPoints);

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];

            var previousHighs = swingPoints
                .Where(s => s.IsHigh && s.Index < i)
                .OrderByDescending(s => s.Index)
                .Take(2)
                .ToList();

            var previousLows = swingPoints
                .Where(s => !s.IsHigh && s.Index < i)
                .OrderByDescending(s => s.Index)
                .Take(2)
                .ToList();

            if (previousHighs.Count < 2 || previousLows.Count < 2) continue;

            var lastHigh = previousHighs[0];
            var lastLow = previousLows[0];

            // Bullish structure break (price closes above previous swing high)
            if (candle.Close > lastHigh.Price)
            {
                var structureType = trend == "down" ? StructureBreak.BullishCHOCH : StructureBreak.BullishBOS;
                structures.Add(new MarketStructure
                {
                    Index = i,
                    Timestamp = candle.Timestamp,
                    Type = structureType,
                    BreakLevel = lastHigh.Price
                });
                trend = "up";
            }

            // Bearish structure break (price closes below previous swing low)
            if (candle.Close < lastLow.Price)
            {
                var structureType = trend == "up" ? StructureBreak.BearishCHOCH : StructureBreak.BearishBOS;
                structures.Add(new MarketStructure
                {
                    Index = i,
                    Timestamp = candle.Timestamp,
                    Type = structureType,
                    BreakLevel = lastLow.Price
                });
                trend = "down";
            }
        }

        return structures;
    }

    private static string DetermineTrend(List<LiquidityZone> swingPoints)
    {
        var recentHighs = swingPoints.Where(s => s.IsHigh).OrderByDescending(s => s.Index).Take(2).ToList();
        var recentLows = swingPoints.Where(s => !s.IsHigh).OrderByDescending(s => s.Index).Take(2).ToList();

        if (recentHighs.Count == 2 && recentLows.Count == 2)
        {
            bool higherHighs = recentHighs[0].Price > recentHighs[1].Price;
            bool higherLows = recentLows[0].Price > recentLows[1].Price;
            if (higherHighs && higherLows) return "up";

            bool lowerHighs = recentHighs[0].Price < recentHighs[1].Price;
            bool lowerLows = recentLows[0].Price < recentLows[1].Price;
            if (lowerHighs && lowerLows) return "down";
        }

        return "sideways";
    }
}
