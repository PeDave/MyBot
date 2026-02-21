namespace MyBot.Backtesting.Models;

/// <summary>A snapshot of portfolio value at a specific point in time.</summary>
public class PortfolioSnapshot
{
    /// <summary>Timestamp of this snapshot.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Total portfolio value (cash + position value).</summary>
    public decimal TotalValue { get; set; }
    /// <summary>Cash balance at this point in time.</summary>
    public decimal CashBalance { get; set; }
    /// <summary>Value of open positions at this point in time.</summary>
    public decimal PositionValue { get; set; }
}
