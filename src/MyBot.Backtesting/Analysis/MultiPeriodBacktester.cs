using MyBot.Backtesting.Analysis;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Analysis;

/// <summary>Defines a named time period for multi-period backtesting.</summary>
public class PeriodDefinition
{
    /// <summary>Short label for the period (e.g., "Bull 2020-2021").</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Human-readable description (e.g., "Major bull run").</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Start date (inclusive).</summary>
    public DateTime StartDate { get; set; }
    /// <summary>End date (inclusive).</summary>
    public DateTime EndDate { get; set; }
}

/// <summary>Backtest result for a single period, enriched with regime information.</summary>
public class PeriodResult
{
    /// <summary>Period definition.</summary>
    public PeriodDefinition Period { get; set; } = new();
    /// <summary>Backtest result for the period.</summary>
    public BacktestResult BacktestResult { get; set; } = new();
    /// <summary>Detected market regime for the period (may be null if insufficient data).</summary>
    public MarketRegime? Regime { get; set; }
    /// <summary>Number of candles available for the period.</summary>
    public int CandleCount { get; set; }
}

/// <summary>Aggregated result across all tested periods.</summary>
public class MultiPeriodResult
{
    /// <summary>Results for each individual period.</summary>
    public List<PeriodResult> PeriodResults { get; set; } = new();
    /// <summary>Combined return across all periods (compound).</summary>
    public decimal OverallReturn { get; set; }
    /// <summary>Average Sharpe ratio across all periods.</summary>
    public decimal AverageSharpe { get; set; }
    /// <summary>Total number of trades across all periods.</summary>
    public int TotalTrades { get; set; }
}

/// <summary>
/// Tests a strategy across multiple predefined time periods and aggregates the results.
/// </summary>
public class MultiPeriodBacktester
{
    private readonly BacktestEngine _engine;

    /// <summary>
    /// Standard periods for multi-period analysis covering major market phases.
    /// </summary>
    public static readonly IReadOnlyList<PeriodDefinition> StandardPeriods = new List<PeriodDefinition>
    {
        new() { Label = "Bull 2020-2021",   Description = "Major bull run",        StartDate = new DateTime(2020, 1, 1),  EndDate = new DateTime(2021, 12, 31) },
        new() { Label = "Bear 2022",        Description = "Bear market crash",     StartDate = new DateTime(2022, 1, 1),  EndDate = new DateTime(2022, 12, 31) },
        new() { Label = "Recovery 2023",    Description = "Recovery year",         StartDate = new DateTime(2023, 1, 1),  EndDate = new DateTime(2023, 12, 31) },
        new() { Label = "Bull 2024",        Description = "Bull market",           StartDate = new DateTime(2024, 1, 1),  EndDate = new DateTime(2024, 12, 31) },
        new() { Label = "Current 2025-2026",Description = "Current period",        StartDate = new DateTime(2025, 1, 1),  EndDate = new DateTime(2026, 2, 22)  },
    };

    /// <summary>Initializes a new <see cref="MultiPeriodBacktester"/>.</summary>
    public MultiPeriodBacktester(BacktestEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Tests the strategy across all given periods using candles filtered from <paramref name="allCandles"/>.
    /// </summary>
    /// <param name="strategy">Strategy to test.</param>
    /// <param name="allCandles">Full historical dataset spanning all periods.</param>
    /// <param name="config">Backtest configuration.</param>
    /// <param name="periods">Periods to test; defaults to <see cref="StandardPeriods"/> if null.</param>
    public MultiPeriodResult RunMultiPeriod(
        Strategies.IBacktestStrategy strategy,
        List<OHLCVCandle> allCandles,
        BacktestConfig config,
        IEnumerable<PeriodDefinition>? periods = null)
    {
        var periodList = (periods ?? StandardPeriods).ToList();
        var periodResults = new List<PeriodResult>();

        foreach (var period in periodList)
        {
            var periodCandles = allCandles
                .Where(c => c.Timestamp >= period.StartDate && c.Timestamp <= period.EndDate)
                .ToList();

            if (periodCandles.Count < 50)
            {
                Console.WriteLine($"  Skipping {period.Label}: insufficient data ({periodCandles.Count} candles).");
                continue;
            }

            strategy.Initialize(new Strategies.StrategyParameters());

            BacktestResult backtestResult;
            try
            {
                backtestResult = _engine.RunBacktest(strategy, periodCandles, config.InitialBalance, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error running {period.Label}: {ex.Message}");
                continue;
            }

            // Detect regime at the midpoint of the period
            var midIndex = periodCandles.Count / 2;
            var regime = MarketRegimeDetector.DetectRegime(periodCandles, midIndex);

            periodResults.Add(new PeriodResult
            {
                Period = period,
                BacktestResult = backtestResult,
                Regime = regime,
                CandleCount = periodCandles.Count
            });
        }

        if (periodResults.Count == 0)
            return new MultiPeriodResult();

        // Compound return: simulate chaining each period's return
        var compoundFactor = periodResults.Aggregate(1m,
            (acc, pr) => acc * (1m + pr.BacktestResult.Metrics.TotalReturnPercentage / 100m));
        var overallReturn = (compoundFactor - 1m) * 100m;

        var avgSharpe = periodResults.Average(pr => pr.BacktestResult.Metrics.SharpeRatio);
        var totalTrades = periodResults.Sum(pr => pr.BacktestResult.Metrics.TotalTrades);

        return new MultiPeriodResult
        {
            PeriodResults = periodResults,
            OverallReturn = overallReturn,
            AverageSharpe = avgSharpe,
            TotalTrades = totalTrades
        };
    }
}
