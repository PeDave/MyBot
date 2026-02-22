using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Indicators;

/// <summary>
/// Represents an Order Block - the last opposing candle before a strong impulse move.
/// </summary>
public class OrderBlock
{
    /// <summary>Index of the order block candle.</summary>
    public int Index { get; set; }
    /// <summary>Timestamp of the order block candle.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Top of the order block range (candle high).</summary>
    public decimal BlockTop { get; set; }
    /// <summary>Bottom of the order block range (candle low).</summary>
    public decimal BlockBottom { get; set; }
    /// <summary>Midpoint of the order block, used as a reference entry price.</summary>
    public decimal MidPoint => (BlockTop + BlockBottom) / 2;
    /// <summary>True = bullish OB (last bearish candle before bullish impulse); false = bearish OB.</summary>
    public bool IsBullish { get; set; }
    /// <summary>Whether this order block has already been used for an entry.</summary>
    public bool IsUsed { get; set; } = false;
}

/// <summary>
/// Detects Order Blocks in OHLCV candle data.
/// </summary>
public static class OrderBlockDetector
{
    /// <summary>
    /// Detects Order Blocks - the last opposing candle before a strong impulse move.
    /// A bullish OB is the last bearish candle before a strong bullish move;
    /// a bearish OB is the last bullish candle before a strong bearish move.
    /// </summary>
    /// <param name="candles">Ordered list of OHLCV candles.</param>
    /// <param name="minImpulsePercent">Minimum percentage move required to qualify as an impulse (default 1.5%).</param>
    public static List<OrderBlock> DetectOrderBlocks(
        List<OHLCVCandle> candles,
        decimal minImpulsePercent = 1.5m)
    {
        var orderBlocks = new List<OrderBlock>();

        for (int i = 1; i < candles.Count - 1; i++)
        {
            var current = candles[i];
            var next = candles[i + 1];

            // Bullish Order Block: last bearish candle before strong bullish impulse
            if (current.Close < current.Open)
            {
                var nextMove = next.Open > 0 ? (next.Close - next.Open) / next.Open * 100 : 0;
                if (next.Close > next.Open && nextMove >= minImpulsePercent)
                {
                    orderBlocks.Add(new OrderBlock
                    {
                        Index = i,
                        Timestamp = current.Timestamp,
                        BlockTop = current.High,
                        BlockBottom = current.Low,
                        IsBullish = true
                    });
                }
            }

            // Bearish Order Block: last bullish candle before strong bearish impulse
            if (current.Close > current.Open)
            {
                var nextMove = next.Open > 0 ? (next.Open - next.Close) / next.Open * 100 : 0;
                if (next.Close < next.Open && nextMove >= minImpulsePercent)
                {
                    orderBlocks.Add(new OrderBlock
                    {
                        Index = i,
                        Timestamp = current.Timestamp,
                        BlockTop = current.High,
                        BlockBottom = current.Low,
                        IsBullish = false
                    });
                }
            }
        }

        return orderBlocks;
    }
}
