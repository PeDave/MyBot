namespace MyBot.Backtesting.Models;

/// <summary>Direction of a trade.</summary>
public enum TradeDirection { Long, Short }

/// <summary>Represents an individual completed or open trade during backtesting.</summary>
public class Trade
{
    /// <summary>Unique trade identifier.</summary>
    public int Id { get; set; }
    /// <summary>Time the trade was entered.</summary>
    public DateTime EntryTime { get; set; }
    /// <summary>Time the trade was exited (null if still open).</summary>
    public DateTime? ExitTime { get; set; }
    /// <summary>Entry price.</summary>
    public decimal EntryPrice { get; set; }
    /// <summary>Exit price (null if still open).</summary>
    public decimal? ExitPrice { get; set; }
    /// <summary>Quantity traded.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Trade direction (Long or Short).</summary>
    public TradeDirection Direction { get; set; }
    /// <summary>Profit or loss in absolute terms.</summary>
    public decimal ProfitLoss { get; set; }
    /// <summary>Profit or loss as a percentage of entry cost.</summary>
    public decimal ProfitLossPercentage { get; set; }
    /// <summary>Total fees paid for this trade (entry + exit).</summary>
    public decimal Fees { get; set; }
    /// <summary>Trading symbol.</summary>
    public string Symbol { get; set; } = string.Empty;
}
