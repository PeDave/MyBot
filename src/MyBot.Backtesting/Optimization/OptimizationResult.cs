using MyBot.Backtesting.Models;
using MyBot.Backtesting.Strategies;

namespace MyBot.Backtesting.Optimization;

/// <summary>Metric to optimize for during parameter search.</summary>
public enum OptimizationMetric
{
    /// <summary>Maximize total return percentage.</summary>
    TotalReturn,
    /// <summary>Maximize Sharpe ratio (return/volatility).</summary>
    SharpeRatio,
    /// <summary>Maximize Sortino ratio (return/downside volatility).</summary>
    SortinoRatio,
    /// <summary>Maximize profit factor (gross profit / gross loss).</summary>
    ProfitFactor,
    /// <summary>Maximize win rate.</summary>
    WinRate,
    /// <summary>Custom metric defined by the caller.</summary>
    Custom
}

/// <summary>Stores the result of a single parameter combination test.</summary>
public class ParameterTestResult
{
    /// <summary>Parameters that were tested.</summary>
    public StrategyParameters Parameters { get; set; } = new();
    /// <summary>Value of the optimized metric for these parameters.</summary>
    public decimal MetricValue { get; set; }
    /// <summary>Total return percentage.</summary>
    public decimal TotalReturn { get; set; }
    /// <summary>Sharpe ratio.</summary>
    public decimal SharpeRatio { get; set; }
    /// <summary>Maximum drawdown percentage.</summary>
    public decimal MaxDrawdown { get; set; }
    /// <summary>Total number of completed trades.</summary>
    public int TotalTrades { get; set; }
    /// <summary>Win rate (0–1).</summary>
    public decimal WinRate { get; set; }
}

/// <summary>Complete result of a parameter optimization run.</summary>
public class OptimizationResult
{
    /// <summary>Parameters that produced the best metric value.</summary>
    public StrategyParameters BestParameters { get; set; } = new();
    /// <summary>Full backtest result using the best parameters.</summary>
    public BacktestResult BestBacktestResult { get; set; } = new();
    /// <summary>Best metric value achieved.</summary>
    public decimal BestMetricValue { get; set; }
    /// <summary>The metric that was optimized.</summary>
    public OptimizationMetric OptimizedFor { get; set; }
    /// <summary>Results for every parameter combination tested.</summary>
    public List<ParameterTestResult> AllResults { get; set; } = new();
    /// <summary>Total wall-clock time taken for the optimization.</summary>
    public TimeSpan OptimizationDuration { get; set; }
    /// <summary>Total number of parameter combinations tested.</summary>
    public int TotalCombinationsTested { get; set; }
}
