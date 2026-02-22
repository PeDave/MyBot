using MyBot.Backtesting.Analysis;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Optimization;

namespace MyBot.Backtesting.Reports;

/// <summary>Generates advanced formatted reports for multi-period, walk-forward, and deep optimization results.</summary>
public class AdvancedReportGenerator
{
    /// <summary>Maximum acceptable out-of-sample degradation percentage before flagging as possible overfitting.</summary>
    private const decimal MaxAcceptableDegradation = 50m;
    /// <summary>Prints a formatted multi-period analysis report to the console.</summary>
    public void PrintMultiPeriodResults(MultiPeriodResult result)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 60));
        Console.WriteLine("  MULTI-PERIOD ANALYSIS");
        Console.WriteLine(new string('═', 60));

        foreach (var pr in result.PeriodResults)
        {
            var m = pr.BacktestResult.Metrics;
            Console.WriteLine();
            Console.WriteLine($"{pr.Period.Label} ({pr.Period.Description})");
            Console.WriteLine($"Period: {pr.Period.StartDate:yyyy-MM-dd} to {pr.Period.EndDate:yyyy-MM-dd}");

            if (pr.Regime != null)
            {
                var adxStr = pr.Regime.Adx.HasValue ? $"ADX={pr.Regime.Adx.Value:F1}" : "ADX=N/A";
                Console.WriteLine($"Regime: {pr.Regime.TrendRegime} | Volatility: {pr.Regime.VolatilityRegime} | Phase: {pr.Regime.MarketPhase} | {adxStr}");
            }

            var returnSign = m.TotalReturnPercentage >= 0 ? "+" : "";
            Console.WriteLine($"Return: {returnSign}{m.TotalReturnPercentage:F2}% | Sharpe: {m.SharpeRatio:F2} | MaxDD: {m.MaxDrawdownPercentage:F1}%");
            Console.WriteLine($"Trades: {m.TotalTrades} | Win Rate: {m.WinRate:P1}");
        }

        Console.WriteLine();
        Console.WriteLine(new string('─', 60));
        var overallSign = result.OverallReturn >= 0 ? "+" : "";
        Console.WriteLine($"OVERALL: Return: {overallSign}{result.OverallReturn:F2}% | Avg Sharpe: {result.AverageSharpe:F2}");
        Console.WriteLine(new string('═', 60));
    }

    /// <summary>Prints a formatted walk-forward optimization report to the console.</summary>
    public void PrintWalkForwardResults(WalkForwardResult result)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 60));
        Console.WriteLine("  WALK-FORWARD OPTIMIZATION RESULTS");
        Console.WriteLine(new string('═', 60));

        for (var i = 0; i < result.Windows.Count; i++)
        {
            var w = result.Windows[i];
            Console.WriteLine();
            Console.WriteLine($"Window {i + 1}:");
            Console.WriteLine($"  In-Sample:  {w.InSampleStart:yyyy-MM-dd} to {w.InSampleEnd:yyyy-MM-dd}");
            Console.WriteLine($"  Out-Sample: {w.OutOfSampleStart:yyyy-MM-dd} to {w.OutOfSampleEnd:yyyy-MM-dd}");

            if (w.OptimalParameters.Count > 0)
            {
                var paramStr = string.Join(", ", w.OptimalParameters.Select(p => $"{p.Key}={p.Value}"));
                Console.WriteLine($"  Best Params: {paramStr}");
            }

            var isReturn = w.InSampleResult.Metrics.TotalReturnPercentage;
            var oosReturn = w.OutOfSampleResult.Metrics.TotalReturnPercentage;
            var degradation = isReturn - oosReturn;

            Console.WriteLine($"  In-Sample:  Return: {isReturn:+0.00;-0.00}% | Sharpe: {w.InSampleResult.Metrics.SharpeRatio:F2}");
            Console.WriteLine($"  Out-Sample: Return: {oosReturn:+0.00;-0.00}% | Sharpe: {w.OutOfSampleResult.Metrics.SharpeRatio:F2}");

            var degradationStatus = degradation <= MaxAcceptableDegradation ? "✓" : "⚠ OVERFITTING";
            Console.WriteLine($"  Degradation: {degradation:F1}% {degradationStatus}");
        }

        Console.WriteLine();
        Console.WriteLine(new string('─', 60));
        var avgStatus = result.DegradationPercent <= MaxAcceptableDegradation ? "✓ (acceptable < 50%)" : "⚠ WARNING: possible overfitting";
        Console.WriteLine($"Average Degradation: {result.DegradationPercent:F1}% {avgStatus}");
        Console.WriteLine(new string('═', 60));
    }

    /// <summary>Prints the top 10 parameter sets from a deep optimization run.</summary>
    public void PrintDeepOptimizationTop10(OptimizationResult result, string strategyName = "")
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 60));
        if (!string.IsNullOrEmpty(strategyName))
            Console.WriteLine($"  DEEP PARAMETER OPTIMIZATION - {strategyName}");
        else
            Console.WriteLine("  DEEP PARAMETER OPTIMIZATION RESULTS");
        Console.WriteLine(new string('═', 60));

        Console.WriteLine($"\nTested {result.TotalCombinationsTested} parameter combinations.");
        Console.WriteLine($"Completed in {result.OptimizationDuration.TotalMinutes:F0}m {result.OptimizationDuration.Seconds:D2}s");
        Console.WriteLine("\nTOP 10 PARAMETER SETS:\n");

        var top10 = result.AllResults.Take(10).ToList();
        for (var i = 0; i < top10.Count; i++)
        {
            var r = top10[i];
            var returnSign = r.TotalReturn >= 0 ? "+" : "";
            Console.WriteLine($"#{i + 1,-3} Metric: {r.MetricValue:F3} | Return: {returnSign}{r.TotalReturn:F2}% | Sharpe: {r.SharpeRatio:F2} | DD: -{r.MaxDrawdown:F2}%");
            var paramStr = string.Join(", ", r.Parameters.Select(p => $"{p.Key}={p.Value}"));
            Console.WriteLine($"     {paramStr}");
        }

        Console.WriteLine(new string('═', 60));
    }

    /// <summary>Exports multi-period results to a CSV file.</summary>
    public void ExportMultiPeriodToCsv(MultiPeriodResult result, string filePath)
    {
        var lines = new List<string> { "Period,Description,StartDate,EndDate,TrendRegime,VolatilityRegime,Phase,Return%,Sharpe,MaxDD%,Trades,WinRate" };
        foreach (var pr in result.PeriodResults)
        {
            var m = pr.BacktestResult.Metrics;
            var trend = pr.Regime?.TrendRegime.ToString() ?? "N/A";
            var vol = pr.Regime?.VolatilityRegime.ToString() ?? "N/A";
            var phase = pr.Regime?.MarketPhase.ToString() ?? "N/A";
            lines.Add($"\"{pr.Period.Label}\",\"{pr.Period.Description}\",{pr.Period.StartDate:yyyy-MM-dd},{pr.Period.EndDate:yyyy-MM-dd},{trend},{vol},{phase},{m.TotalReturnPercentage:F4},{m.SharpeRatio:F4},{m.MaxDrawdownPercentage:F4},{m.TotalTrades},{m.WinRate:F4}");
        }
        lines.Add($"\"OVERALL\",,,,,,, {result.OverallReturn:F4},{result.AverageSharpe:F4},,{result.TotalTrades},");
        File.WriteAllLines(filePath, lines);
        Console.WriteLine($"Multi-period results exported to: {filePath}");
    }
}
