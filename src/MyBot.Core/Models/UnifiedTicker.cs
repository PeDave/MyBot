namespace MyBot.Core.Models;

/// <summary>Represents current price and market data for a symbol.</summary>
public class UnifiedTicker
{
    /// <summary>The trading symbol.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Last traded price.</summary>
    public decimal LastPrice { get; set; }

    /// <summary>Best bid price.</summary>
    public decimal BidPrice { get; set; }

    /// <summary>Best ask price.</summary>
    public decimal AskPrice { get; set; }

    /// <summary>24-hour high price.</summary>
    public decimal High24h { get; set; }

    /// <summary>24-hour low price.</summary>
    public decimal Low24h { get; set; }

    /// <summary>24-hour volume in base asset.</summary>
    public decimal Volume24h { get; set; }

    /// <summary>24-hour volume in quote asset.</summary>
    public decimal QuoteVolume24h { get; set; }

    /// <summary>24-hour price change percentage.</summary>
    public decimal ChangePercent24h { get; set; }

    /// <summary>The exchange this ticker is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp of the ticker data.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
