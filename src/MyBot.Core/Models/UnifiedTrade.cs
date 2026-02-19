namespace MyBot.Core.Models;

/// <summary>Represents a trade/transaction in a standardized format.</summary>
public class UnifiedTrade
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

    /// <summary>Side of the taker (buyer or seller).</summary>
    public OrderSide TakerSide { get; set; }

    /// <summary>When the trade occurred.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>The exchange this trade is from.</summary>
    public string Exchange { get; set; } = string.Empty;
}
