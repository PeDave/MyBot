using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Strategies;

namespace MyBot.Backtesting.Optimization;

/// <summary>
/// Deep optimizer: performs exhaustive parallel grid search across all parameter combinations
/// using a custom multi-objective fitness function.
/// Fitness = (Return × WinRate × ProfitFactor) / MaxDrawdown
/// </summary>
public class DeepOptimizer
{
    private readonly BacktestEngine _engine;
    private readonly ILogger<DeepOptimizer> _logger;

    /// <summary>Initializes a new <see cref="DeepOptimizer"/>.</summary>
    public DeepOptimizer(BacktestEngine engine, ILogger<DeepOptimizer> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Runs an exhaustive parallel grid search over all parameter combinations.
    /// Progress is reported to the console every 5% of combinations completed.
    /// </summary>
    /// <param name="strategyFactory">
    /// Factory that creates a fresh, independent strategy instance for each thread.
    /// </param>
    /// <param name="historicalData">Historical candles to optimize on.</param>
    /// <param name="parameterGrid">Parameter ranges to search.</param>
    /// <param name="config">Backtest configuration.</param>
    public OptimizationResult RunDeepOptimization(
        Func<IBacktestStrategy> strategyFactory,
        List<OHLCVCandle> historicalData,
        ParameterGrid parameterGrid,
        BacktestConfig config)
    {
        if (historicalData.Count == 0)
            throw new ArgumentException("Historical data cannot be empty.", nameof(historicalData));

        var combinations = GenerateCombinations(parameterGrid);
        var totalCombinations = combinations.Count;

        Console.WriteLine($"Testing {totalCombinations} parameter combinations...");
        _logger.LogInformation("DeepOptimizer: {Count} combinations", totalCombinations);

        var startTime = DateTime.UtcNow;
        var allResults = new ConcurrentBag<ParameterTestResult>();
        var completedCount = 0;
        var reportInterval = Math.Max(1, totalCombinations / 20); // report every 5%

        Parallel.ForEach(
            combinations,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            parameters =>
            {
                var strategy = strategyFactory();
                strategy.Initialize(parameters);

                BacktestResult backtestResult;
                try
                {
                    backtestResult = _engine.RunBacktest(strategy, historicalData, config.InitialBalance, config);
                }
                catch
                {
                    return;
                }

                var metricValue = CalculateFitness(backtestResult);

                allResults.Add(new ParameterTestResult
                {
                    Parameters = parameters,
                    MetricValue = metricValue,
                    TotalReturn = backtestResult.Metrics.TotalReturnPercentage,
                    SharpeRatio = backtestResult.Metrics.SharpeRatio,
                    MaxDrawdown = backtestResult.Metrics.MaxDrawdownPercentage,
                    TotalTrades = backtestResult.Metrics.TotalTrades,
                    WinRate = backtestResult.Metrics.WinRate
                });

                var completed = Interlocked.Increment(ref completedCount);
                if (completed % reportInterval == 0 || completed == totalCombinations)
                {
                    var pct = completed * 100 / totalCombinations;
                    Console.WriteLine($"Progress: {pct,3}% ({completed}/{totalCombinations})");
                }
            });

        var duration = DateTime.UtcNow - startTime;
        var sortedResults = allResults.OrderByDescending(r => r.MetricValue).ToList();
        var best = sortedResults.FirstOrDefault() ?? new ParameterTestResult();

        // Run a final single-threaded backtest to capture the full BacktestResult for the best params
        var bestStrategy = strategyFactory();
        bestStrategy.Initialize(best.Parameters);
        var bestBacktestResult = historicalData.Count > 0
            ? _engine.RunBacktest(bestStrategy, historicalData, config.InitialBalance, config)
            : new BacktestResult();

        return new OptimizationResult
        {
            BestParameters = best.Parameters,
            BestBacktestResult = bestBacktestResult,
            BestMetricValue = best.MetricValue,
            OptimizedFor = OptimizationMetric.Custom,
            AllResults = sortedResults,
            OptimizationDuration = duration,
            TotalCombinationsTested = sortedResults.Count
        };
    }

    /// <summary>
    /// Custom fitness function: (Return × WinRate × ProfitFactor) / MaxDrawdown.
    /// Handles edge cases such as zero drawdown and infinite profit factor.
    /// </summary>
    private static decimal CalculateFitness(BacktestResult result)
    {
        var m = result.Metrics;
        if (m.TotalTrades == 0) return decimal.MinValue;

        var returnScore = Math.Max(0m, m.TotalReturnPercentage);
        var winRate = m.WinRate > 0 ? m.WinRate : 0.01m;
        var profitFactor = m.ProfitFactor == decimal.MaxValue ? 10m : Math.Max(0m, m.ProfitFactor);
        var maxDrawdown = m.MaxDrawdownPercentage > 0 ? m.MaxDrawdownPercentage : 1m;

        return (returnScore * winRate * profitFactor) / maxDrawdown;
    }

    private static List<StrategyParameters> GenerateCombinations(ParameterGrid grid)
    {
        var keys = grid.Keys.ToList();
        var valueLists = keys.Select(k => grid[k].GetValues().ToList()).ToList();

        var result = new List<StrategyParameters> { new() };

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var values = valueLists[i];
            var expanded = new List<StrategyParameters>();
            foreach (var existing in result)
            {
                foreach (var val in values)
                {
                    var newParams = new StrategyParameters();
                    foreach (var kv in existing) newParams[kv.Key] = kv.Value;
                    newParams[key] = val;
                    expanded.Add(newParams);
                }
            }
            result = expanded;
        }

        return result;
    }
}
