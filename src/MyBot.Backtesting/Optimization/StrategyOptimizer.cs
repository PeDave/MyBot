using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Strategies;

namespace MyBot.Backtesting.Optimization;

/// <summary>
/// Grid-search optimizer: tests all combinations of parameters in a <see cref="ParameterGrid"/>
/// and returns the combination that maximizes the chosen <see cref="OptimizationMetric"/>.
/// </summary>
public class StrategyOptimizer
{
    private readonly BacktestEngine _engine;
    private readonly ILogger<StrategyOptimizer> _logger;

    /// <summary>Initializes a new <see cref="StrategyOptimizer"/>.</summary>
    public StrategyOptimizer(BacktestEngine engine, ILogger<StrategyOptimizer> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Runs a grid-search over all parameter combinations and returns the result
    /// that maximizes the specified <paramref name="metric"/>.
    /// </summary>
    /// <param name="strategy">The strategy to optimize.</param>
    /// <param name="historicalData">Historical candles to test on.</param>
    /// <param name="parameterGrid">Parameter ranges to search over.</param>
    /// <param name="config">Backtest configuration.</param>
    /// <param name="metric">Metric to maximize.</param>
    public OptimizationResult OptimizeParameters(
        IBacktestStrategy strategy,
        List<OHLCVCandle> historicalData,
        ParameterGrid parameterGrid,
        BacktestConfig config,
        OptimizationMetric metric = OptimizationMetric.SharpeRatio)
    {
        var combinations = GenerateCombinations(parameterGrid);
        var totalCombinations = combinations.Count;

        Console.WriteLine($"Testing {totalCombinations} parameter combinations...");
        _logger.LogInformation("Starting grid search: {Count} combinations, metric={Metric}", totalCombinations, metric);

        var startTime = DateTime.UtcNow;
        var allResults = new List<ParameterTestResult>(totalCombinations);
        var bestMetric = decimal.MinValue;
        StrategyParameters bestParams = new();
        BacktestResult bestBacktestResult = new();

        for (var i = 0; i < totalCombinations; i++)
        {
            var parameters = combinations[i];
            strategy.Initialize(parameters);

            BacktestResult backtestResult;
            try
            {
                backtestResult = _engine.RunBacktest(strategy, historicalData, config.InitialBalance, config);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backtest failed for combination {Index}/{Total}", i + 1, totalCombinations);
                continue;
            }

            var metricValue = EvaluateMetric(backtestResult, metric);

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

            if (metricValue > bestMetric)
            {
                bestMetric = metricValue;
                bestParams = parameters;
                bestBacktestResult = backtestResult;
            }

            // Progress reporting every 10% or every 50 combinations
            if ((i + 1) % Math.Max(1, totalCombinations / 10) == 0 || i + 1 == totalCombinations)
            {
                var progress = (i + 1) * 100 / totalCombinations;
                Console.WriteLine($"Progress: {progress,3}% ({i + 1}/{totalCombinations})");
            }
        }

        var duration = DateTime.UtcNow - startTime;

        return new OptimizationResult
        {
            BestParameters = bestParams,
            BestBacktestResult = bestBacktestResult,
            BestMetricValue = bestMetric,
            OptimizedFor = metric,
            AllResults = allResults,
            OptimizationDuration = duration,
            TotalCombinationsTested = allResults.Count
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

    /// <summary>
    /// Generates all combinations of parameter values from the grid using a Cartesian product.
    /// </summary>
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
