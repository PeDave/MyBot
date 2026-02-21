namespace MyBot.Core.Models;

/// <summary>
/// Unified OHLCV kline/candle data model across all exchanges.
/// </summary>
public class UnifiedKline
{
    /// <summary>Candle open time.</summary>
    public DateTime OpenTime { get; set; }

    /// <summary>Candle close time.</summary>
    public DateTime CloseTime { get; set; }

    /// <summary>Open price.</summary>
    public decimal Open { get; set; }

    /// <summary>High price.</summary>
    public decimal High { get; set; }

    /// <summary>Low price.</summary>
    public decimal Low { get; set; }

    /// <summary>Close price.</summary>
    public decimal Close { get; set; }

    /// <summary>Volume.</summary>
    public decimal Volume { get; set; }

    /// <summary>Quote asset volume (optional).</summary>
    public decimal? QuoteVolume { get; set; }

    /// <summary>Number of trades (optional).</summary>
    public int? TradeCount { get; set; }

    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
}
