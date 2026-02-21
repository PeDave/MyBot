using MyBot.Backtesting.Engine;

namespace MyBot.Backtesting.Models;

/// <summary>Complete results of a backtest run.</summary>
public class BacktestResult
{
    /// <summary>Name of the strategy used.</summary>
    public string StrategyName { get; set; } = string.Empty;
    /// <summary>Symbol that was tested.</summary>
    public string Symbol { get; set; } = string.Empty;
    /// <summary>Timeframe used (e.g., "1h").</summary>
    public string Timeframe { get; set; } = string.Empty;
    /// <summary>Start date of the backtest period.</summary>
    public DateTime StartDate { get; set; }
    /// <summary>End date of the backtest period.</summary>
    public DateTime EndDate { get; set; }
    /// <summary>Initial portfolio balance.</summary>
    public decimal InitialBalance { get; set; }
    /// <summary>Final portfolio balance.</summary>
    public decimal FinalBalance { get; set; }
    /// <summary>Calculated performance metrics.</summary>
    public PerformanceMetrics Metrics { get; set; } = new();
    /// <summary>List of all completed trades.</summary>
    public List<Trade> Trades { get; set; } = new();
    /// <summary>Equity curve (portfolio value over time).</summary>
    public List<PortfolioSnapshot> EquityCurve { get; set; } = new();
    /// <summary>Configuration used for this backtest.</summary>
    public BacktestConfig Config { get; set; } = new();
}
