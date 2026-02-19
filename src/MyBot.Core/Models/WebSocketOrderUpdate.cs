namespace MyBot.Core.Models;

/// <summary>Represents a real-time user order status update received via WebSocket.</summary>
public class WebSocketOrderUpdate
{
    /// <summary>The exchange-specific order ID.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Optional client-assigned order ID.</summary>
    public string? ClientOrderId { get; set; }

    /// <summary>The trading symbol (e.g., "BTCUSDT").</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Order side (buy/sell).</summary>
    public OrderSide Side { get; set; }

    /// <summary>Order type (market/limit/etc).</summary>
    public OrderType Type { get; set; }

    /// <summary>Current order status.</summary>
    public OrderStatus Status { get; set; }

    /// <summary>Original order quantity.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Quantity filled so far.</summary>
    public decimal QuantityFilled { get; set; }

    /// <summary>Limit price (null for market orders).</summary>
    public decimal? Price { get; set; }

    /// <summary>Average fill price.</summary>
    public decimal? AveragePrice { get; set; }

    /// <summary>The exchange this update is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp of this update.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
