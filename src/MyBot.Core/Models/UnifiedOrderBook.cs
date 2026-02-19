namespace MyBot.Core.Models;

/// <summary>Represents an order book entry (bid or ask).</summary>
public class OrderBookEntry
{
    /// <summary>Price level.</summary>
    public decimal Price { get; set; }

    /// <summary>Quantity available at this price level.</summary>
    public decimal Quantity { get; set; }
}

/// <summary>Represents a market order book in a standardized format.</summary>
public class UnifiedOrderBook
{
    /// <summary>The trading symbol.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>List of bid (buy) orders, sorted by price descending.</summary>
    public List<OrderBookEntry> Bids { get; set; } = new();

    /// <summary>List of ask (sell) orders, sorted by price ascending.</summary>
    public List<OrderBookEntry> Asks { get; set; } = new();

    /// <summary>The exchange this order book is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp of the order book snapshot.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
