using System.Text.Json;
using System.Text.Json.Serialization;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Reports;

/// <summary>Generates formatted reports and exports for backtest results.</summary>
public class BacktestReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Prints a formatted summary of the backtest result to the console.</summary>
    public void PrintSummary(BacktestResult result)
    {
        var m = result.Metrics;
        Console.WriteLine();
        Console.WriteLine(new string('═', 60));
        Console.WriteLine($"  BACKTEST RESULTS: {result.StrategyName}");
        Console.WriteLine(new string('═', 60));
        Console.WriteLine($"  Symbol:        {result.Symbol}  |  Timeframe: {result.Timeframe}");
        Console.WriteLine($"  Period:        {result.StartDate:yyyy-MM-dd} → {result.EndDate:yyyy-MM-dd}");
        Console.WriteLine($"  Duration:      {m.BacktestDuration.Days} days");
        Console.WriteLine(new string('─', 60));
        Console.WriteLine("  RETURNS");
        Console.WriteLine($"    Initial Balance:   {result.InitialBalance,12:C}");
        Console.WriteLine($"    Final Balance:     {result.FinalBalance,12:C}");
        Console.WriteLine($"    Total Return:      {m.TotalReturn,12:C}  ({m.TotalReturnPercentage:+0.00;-0.00}%)");
        Console.WriteLine($"    Annualized Return: {m.AnnualizedReturn,12:F2}%");
        Console.WriteLine(new string('─', 60));
        Console.WriteLine("  RISK METRICS");
        Console.WriteLine($"    Max Drawdown:      {m.MaxDrawdown,12:C}  ({m.MaxDrawdownPercentage:0.00}%)");
        Console.WriteLine($"    Sharpe Ratio:      {m.SharpeRatio,12:F3}");
        Console.WriteLine($"    Sortino Ratio:     {m.SortinoRatio,12:F3}");
        Console.WriteLine(new string('─', 60));
        Console.WriteLine("  TRADE STATISTICS");
        Console.WriteLine($"    Total Trades:      {m.TotalTrades,12}");
        Console.WriteLine($"    Winning Trades:    {m.WinningTrades,12}");
        Console.WriteLine($"    Losing Trades:     {m.LosingTrades,12}");
        Console.WriteLine($"    Win Rate:          {m.WinRate,12:P1}");
        Console.WriteLine($"    Profit Factor:     {(m.ProfitFactor == decimal.MaxValue ? "∞" : m.ProfitFactor.ToString("F2")),12}");
        Console.WriteLine($"    Avg Win:           {m.AverageWin,12:C}");
        Console.WriteLine($"    Avg Loss:          {m.AverageLoss,12:C}");
        Console.WriteLine($"    Largest Win:       {m.LargestWin,12:C}");
        Console.WriteLine($"    Largest Loss:      {m.LargestLoss,12:C}");
        Console.WriteLine($"    Avg Hold (hours):  {m.AverageHoldingPeriodHours,12:F1}");
        Console.WriteLine(new string('═', 60));
    }

    /// <summary>Exports all trades to a CSV file.</summary>
    public void ExportTradesToCsv(BacktestResult result, string filePath)
    {
        var lines = new List<string>
        {
            "Id,Symbol,Direction,EntryTime,ExitTime,EntryPrice,ExitPrice,Quantity,ProfitLoss,ProfitLossPercentage,Fees"
        };
        foreach (var t in result.Trades)
            lines.Add($"{t.Id},{t.Symbol},{t.Direction},{t.EntryTime:O},{t.ExitTime:O},{t.EntryPrice},{t.ExitPrice},{t.Quantity},{t.ProfitLoss},{t.ProfitLossPercentage:F4},{t.Fees}");
        File.WriteAllLines(filePath, lines);
        Console.WriteLine($"Trades exported to: {filePath}");
    }

    /// <summary>Exports the equity curve to a CSV file.</summary>
    public void ExportEquityCurveToCsv(BacktestResult result, string filePath)
    {
        var lines = new List<string> { "Timestamp,TotalValue,CashBalance,PositionValue" };
        foreach (var s in result.EquityCurve)
            lines.Add($"{s.Timestamp:O},{s.TotalValue},{s.CashBalance},{s.PositionValue}");
        File.WriteAllLines(filePath, lines);
        Console.WriteLine($"Equity curve exported to: {filePath}");
    }

    /// <summary>Exports the full backtest result to a JSON file.</summary>
    public void ExportToJson(BacktestResult result, string filePath)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        File.WriteAllText(filePath, json);
        Console.WriteLine($"Full result exported to: {filePath}");
    }

    /// <summary>Prints a comparison table for multiple backtest results.</summary>
    public void PrintComparison(IEnumerable<BacktestResult> results)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 90));
        Console.WriteLine("  STRATEGY COMPARISON");
        Console.WriteLine(new string('═', 90));
        Console.WriteLine($"  {"Strategy",-28} {"Return%",8} {"Sharpe",8} {"MaxDD%",8} {"Trades",8} {"WinRate",8} {"PFactor",8}");
        Console.WriteLine(new string('─', 90));
        foreach (var r in results)
        {
            var m = r.Metrics;
            Console.WriteLine($"  {r.StrategyName,-28} {m.TotalReturnPercentage,7:F2}% {m.SharpeRatio,8:F2} {m.MaxDrawdownPercentage,7:F2}% {m.TotalTrades,8} {m.WinRate,7:P0} {(m.ProfitFactor == decimal.MaxValue ? "∞" : m.ProfitFactor.ToString("F2")),8}");
        }
        Console.WriteLine(new string('═', 90));
    }
}
