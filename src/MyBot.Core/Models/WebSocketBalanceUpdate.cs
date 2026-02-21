namespace MyBot.Core.Models;

/// <summary>Represents a real-time account balance update from a WebSocket stream.</summary>
public class WebSocketBalanceUpdate
{
    /// <summary>The asset/currency symbol (e.g., "BTC", "USDT").</summary>
    public string Asset { get; set; } = string.Empty;

    /// <summary>The available/free balance.</summary>
    public decimal Available { get; set; }

    /// <summary>The locked/reserved balance.</summary>
    public decimal Locked { get; set; }

    /// <summary>The total balance (free + locked).</summary>
    public decimal Total { get; set; }

    /// <summary>The exchange this update is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp of this update.</summary>
    public DateTime Timestamp { get; set; }
}
