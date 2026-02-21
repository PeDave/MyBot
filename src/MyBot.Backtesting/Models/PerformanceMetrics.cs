namespace MyBot.Backtesting.Models;

/// <summary>Comprehensive performance statistics for a backtest run.</summary>
public class PerformanceMetrics
{
    // Returns
    /// <summary>Total absolute return in base currency.</summary>
    public decimal TotalReturn { get; set; }
    /// <summary>Total return as a percentage of initial balance.</summary>
    public decimal TotalReturnPercentage { get; set; }
    /// <summary>Annualized return percentage.</summary>
    public decimal AnnualizedReturn { get; set; }

    // Risk metrics
    /// <summary>Maximum peak-to-trough drawdown in absolute terms.</summary>
    public decimal MaxDrawdown { get; set; }
    /// <summary>Maximum drawdown as a percentage.</summary>
    public decimal MaxDrawdownPercentage { get; set; }
    /// <summary>Sharpe ratio (risk-adjusted return).</summary>
    public decimal SharpeRatio { get; set; }
    /// <summary>Sortino ratio (downside risk-adjusted return).</summary>
    public decimal SortinoRatio { get; set; }

    // Trade statistics
    /// <summary>Total number of completed trades.</summary>
    public int TotalTrades { get; set; }
    /// <summary>Number of winning trades.</summary>
    public int WinningTrades { get; set; }
    /// <summary>Number of losing trades.</summary>
    public int LosingTrades { get; set; }
    /// <summary>Win rate (0 to 1).</summary>
    public decimal WinRate { get; set; }
    /// <summary>Profit factor (gross profit / gross loss).</summary>
    public decimal ProfitFactor { get; set; }
    /// <summary>Average profit on winning trades.</summary>
    public decimal AverageWin { get; set; }
    /// <summary>Average loss on losing trades (positive value).</summary>
    public decimal AverageLoss { get; set; }
    /// <summary>Largest single winning trade profit.</summary>
    public decimal LargestWin { get; set; }
    /// <summary>Largest single losing trade loss (positive value).</summary>
    public decimal LargestLoss { get; set; }

    // Time-based
    /// <summary>Total duration of the backtest.</summary>
    public TimeSpan BacktestDuration { get; set; }
    /// <summary>Average holding period in hours.</summary>
    public decimal AverageHoldingPeriodHours { get; set; }
}
