using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Strategies;

namespace MyBot.Backtesting.Optimization;

/// <summary>One rolling window used in walk-forward optimization.</summary>
public class WalkForwardWindow
{
    /// <summary>Start of the in-sample (training) period.</summary>
    public DateTime InSampleStart { get; set; }
    /// <summary>End of the in-sample (training) period.</summary>
    public DateTime InSampleEnd { get; set; }
    /// <summary>Start of the out-of-sample (test) period.</summary>
    public DateTime OutOfSampleStart { get; set; }
    /// <summary>End of the out-of-sample (test) period.</summary>
    public DateTime OutOfSampleEnd { get; set; }
    /// <summary>Parameters selected as optimal on the in-sample period.</summary>
    public StrategyParameters OptimalParameters { get; set; } = new();
    /// <summary>Backtest result on the in-sample period.</summary>
    public BacktestResult InSampleResult { get; set; } = new();
    /// <summary>Backtest result on the out-of-sample period using the in-sample optimal parameters.</summary>
    public BacktestResult OutOfSampleResult { get; set; } = new();
}

/// <summary>Aggregated result of a walk-forward optimization run.</summary>
public class WalkForwardResult
{
    /// <summary>Parameters that performed best across all out-of-sample windows.</summary>
    public StrategyParameters BestParameters { get; set; } = new();
    /// <summary>All rolling windows tested.</summary>
    public List<WalkForwardWindow> Windows { get; set; } = new();
    /// <summary>Average total return percentage across in-sample windows.</summary>
    public decimal AverageInSampleReturn { get; set; }
    /// <summary>Average total return percentage across out-of-sample windows.</summary>
    public decimal AverageOutOfSampleReturn { get; set; }
    /// <summary>
    /// Performance degradation: how much worse out-of-sample performs vs in-sample (%).
    /// Positive means worse OOS performance; negative means OOS outperforms IS.
    /// </summary>
    public decimal DegradationPercent { get; set; }
}

/// <summary>
/// Walk-forward optimizer: prevents overfitting by repeatedly optimizing on in-sample
/// data and testing on adjacent out-of-sample data across rolling windows.
/// </summary>
public class WalkForwardOptimizer
{
    private readonly BacktestEngine _engine;
    private readonly ILogger<WalkForwardOptimizer> _logger;

    /// <summary>Initializes a new <see cref="WalkForwardOptimizer"/>.</summary>
    public WalkForwardOptimizer(BacktestEngine engine, ILogger<WalkForwardOptimizer> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Performs walk-forward optimization: splits data into rolling in-sample / out-of-sample windows,
    /// optimizes parameters on the in-sample portion and validates on the out-of-sample portion.
    /// </summary>
    /// <param name="strategy">The strategy to optimize.</param>
    /// <param name="historicalData">Full historical dataset ordered by time.</param>
    /// <param name="parameterGrid">Parameter ranges to search.</param>
    /// <param name="config">Backtest configuration.</param>
    /// <param name="inSampleMonths">Number of months for in-sample (training) window.</param>
    /// <param name="outOfSampleMonths">Number of months for out-of-sample (test) window.</param>
    /// <param name="windowSteps">Number of rolling windows to evaluate.</param>
    /// <param name="metric">Metric to optimize on in-sample data.</param>
    public WalkForwardResult Optimize(
        IBacktestStrategy strategy,
        List<OHLCVCandle> historicalData,
        ParameterGrid parameterGrid,
        BacktestConfig config,
        int inSampleMonths = 6,
        int outOfSampleMonths = 2,
        int windowSteps = 3,
        OptimizationMetric metric = OptimizationMetric.SharpeRatio)
    {
        if (historicalData.Count == 0)
            throw new ArgumentException("Historical data cannot be empty.", nameof(historicalData));

        var innerOptimizer = new StrategyOptimizer(_engine, _logger as ILogger<StrategyOptimizer>
            ?? new LoggerAdapter<StrategyOptimizer>(_logger));

        var windows = new List<WalkForwardWindow>();
        var startDate = historicalData[0].Timestamp;

        for (var step = 0; step < windowSteps; step++)
        {
            var inSampleStart = startDate.AddMonths(step * outOfSampleMonths);
            var inSampleEnd = inSampleStart.AddMonths(inSampleMonths);
            var outOfSampleStart = inSampleEnd;
            var outOfSampleEnd = outOfSampleStart.AddMonths(outOfSampleMonths);

            // Guard: ensure we have data for this window
            if (outOfSampleEnd > historicalData[^1].Timestamp)
            {
                _logger.LogInformation("Walk-forward window {Step} exceeds available data; stopping.", step + 1);
                break;
            }

            var inSampleData = historicalData
                .Where(c => c.Timestamp >= inSampleStart && c.Timestamp < inSampleEnd)
                .ToList();
            var outOfSampleData = historicalData
                .Where(c => c.Timestamp >= outOfSampleStart && c.Timestamp < outOfSampleEnd)
                .ToList();

            if (inSampleData.Count < 50 || outOfSampleData.Count < 10)
            {
                _logger.LogWarning("Insufficient data for walk-forward window {Step}; skipping.", step + 1);
                continue;
            }

            Console.WriteLine($"\nWalk-Forward Window {step + 1}/{windowSteps}:");
            Console.WriteLine($"  In-sample:     {inSampleStart:yyyy-MM-dd} → {inSampleEnd:yyyy-MM-dd} ({inSampleData.Count} candles)");
            Console.WriteLine($"  Out-of-sample: {outOfSampleStart:yyyy-MM-dd} → {outOfSampleEnd:yyyy-MM-dd} ({outOfSampleData.Count} candles)");

            // Optimize on in-sample data
            var optResult = innerOptimizer.OptimizeParameters(strategy, inSampleData, parameterGrid, config, metric);

            // Validate on out-of-sample data with the best in-sample parameters
            strategy.Initialize(optResult.BestParameters);
            var oosResult = _engine.RunBacktest(strategy, outOfSampleData, config.InitialBalance, config);

            Console.WriteLine($"  IS return: {optResult.BestBacktestResult.Metrics.TotalReturnPercentage:+0.00;-0.00}%  |  OOS return: {oosResult.Metrics.TotalReturnPercentage:+0.00;-0.00}%");

            windows.Add(new WalkForwardWindow
            {
                InSampleStart = inSampleStart,
                InSampleEnd = inSampleEnd,
                OutOfSampleStart = outOfSampleStart,
                OutOfSampleEnd = outOfSampleEnd,
                OptimalParameters = optResult.BestParameters,
                InSampleResult = optResult.BestBacktestResult,
                OutOfSampleResult = oosResult
            });
        }

        if (windows.Count == 0)
        {
            _logger.LogWarning("No walk-forward windows could be completed.");
            return new WalkForwardResult();
        }

        // Select the parameters from the window with the best OOS metric value
        var bestWindow = windows
            .OrderByDescending(w => EvaluateMetric(w.OutOfSampleResult, metric))
            .First();

        var avgIsReturn = windows.Average(w => w.InSampleResult.Metrics.TotalReturnPercentage);
        var avgOosReturn = windows.Average(w => w.OutOfSampleResult.Metrics.TotalReturnPercentage);

        return new WalkForwardResult
        {
            BestParameters = bestWindow.OptimalParameters,
            Windows = windows,
            AverageInSampleReturn = avgIsReturn,
            AverageOutOfSampleReturn = avgOosReturn,
            DegradationPercent = avgIsReturn - avgOosReturn
        };
    }

    private static decimal EvaluateMetric(BacktestResult result, OptimizationMetric metric)
    {
        return metric switch
        {
            OptimizationMetric.TotalReturn => result.Metrics.TotalReturnPercentage,
            OptimizationMetric.SharpeRatio => result.Metrics.SharpeRatio,
            OptimizationMetric.SortinoRatio => result.Metrics.SortinoRatio,
            OptimizationMetric.ProfitFactor => result.Metrics.ProfitFactor == decimal.MaxValue
                ? 999m
                : result.Metrics.ProfitFactor,
            OptimizationMetric.WinRate => result.Metrics.WinRate,
            _ => result.Metrics.SharpeRatio
        };
    }

    /// <summary>Minimal adapter to allow using a non-generic logger as a generic one.</summary>
    private sealed class LoggerAdapter<T> : ILogger<T>
    {
        private readonly ILogger _inner;
        public LoggerAdapter(ILogger inner) => _inner = inner;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
