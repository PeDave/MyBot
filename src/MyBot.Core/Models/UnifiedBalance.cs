namespace MyBot.Core.Models;

/// <summary>Represents account balance information in a standardized format.</summary>
public class UnifiedBalance
{
    /// <summary>The asset/currency symbol (e.g., "BTC", "USDT").</summary>
    public string Asset { get; set; } = string.Empty;

    /// <summary>The total balance (free + locked).</summary>
    public decimal Total { get; set; }

    /// <summary>The available/free balance.</summary>
    public decimal Available { get; set; }

    /// <summary>The locked/reserved balance (in open orders).</summary>
    public decimal Locked { get; set; }

    /// <summary>The exchange this balance is from.</summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>Timestamp of the balance snapshot.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
