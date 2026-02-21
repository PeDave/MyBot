namespace MyBot.Core.Models;

/// <summary>Represents a real-time price/ticker update from a WebSocket stream.</summary>
public class WebSocketTickerUpdate
{
    /// <summary>The trading symbol (e.g., "BTCUSDT").</summary>
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

    /// <summary>The exchange this update is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp of this update.</summary>
    public DateTime Timestamp { get; set; }
}
