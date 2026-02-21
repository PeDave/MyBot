namespace MyBot.Backtesting.Models;

/// <summary>Unified OHLCV (Open, High, Low, Close, Volume) candlestick data model.</summary>
public class OHLCVCandle
{
    /// <summary>The timestamp of the candle's open time.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Opening price.</summary>
    public decimal Open { get; set; }
    /// <summary>Highest price during the period.</summary>
    public decimal High { get; set; }
    /// <summary>Lowest price during the period.</summary>
    public decimal Low { get; set; }
    /// <summary>Closing price.</summary>
    public decimal Close { get; set; }
    /// <summary>Trading volume during the period.</summary>
    public decimal Volume { get; set; }
    /// <summary>Trading symbol (e.g., "BTCUSDT").</summary>
    public string Symbol { get; set; } = string.Empty;
    /// <summary>Exchange name.</summary>
    public string Exchange { get; set; } = string.Empty;
}
