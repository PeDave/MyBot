namespace MyBot.Core.Models;

/// <summary>Represents a real-time order book update from a WebSocket stream.</summary>
public class WebSocketOrderBookUpdate
{
    /// <summary>The trading symbol.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Updated bid (buy) levels.</summary>
    public List<OrderBookEntry> Bids { get; set; } = new();

    /// <summary>Updated ask (sell) levels.</summary>
    public List<OrderBookEntry> Asks { get; set; } = new();

    /// <summary>The exchange this update is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp of this update.</summary>
    public DateTime Timestamp { get; set; }
}
