namespace MyBot.Core.Models;

/// <summary>Represents a real-time trade execution update from a WebSocket stream.</summary>
public class WebSocketTradeUpdate
{
    /// <summary>Exchange-specific trade ID.</summary>
    public string TradeId { get; set; } = string.Empty;

    /// <summary>The trading symbol.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Price at which the trade occurred.</summary>
    public decimal Price { get; set; }

    /// <summary>Quantity traded.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Quote quantity (price * quantity).</summary>
    public decimal QuoteQuantity { get; set; }

    /// <summary>Side of the taker.</summary>
    public OrderSide TakerSide { get; set; }

    /// <summary>The exchange this update is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp when the trade occurred.</summary>
    public DateTime Timestamp { get; set; }
}
