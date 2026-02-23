using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Data;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.ML;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Reports;
using MyBot.Backtesting.Strategies;
using MyBot.Core.Interfaces;
using MyBot.Exchanges.Bitget;

// ─── Command-line argument parsing ───────────────────────────────────────────
// Usage: dotnet run -- --symbols BTCUSDT,ETHUSDT --start 2020-01-01 --end 2024-12-31
var symbolsArg = GetArg(args, "--symbols") ?? "BTCUSDT";
var startArg   = GetArg(args, "--start")   ?? "2020-01-01";
var endArg     = GetArg(args, "--end");

var symbols      = symbolsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var multiPeriodStart = DateTime.TryParse(startArg, out var parsedStart) ? parsedStart : new DateTime(2020, 1, 1);
var multiPeriodEnd   = endArg != null && DateTime.TryParse(endArg, out var parsedEnd) ? parsedEnd : DateTime.UtcNow;

// ─── Configuration ────────────────────────────────────────────────────────────
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Warning); // suppress noisy SDK logs
});

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  MyBot Backtesting Console – Multi-Strategy + ML Selection");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine($"  Symbols : {string.Join(", ", symbols)}");
Console.WriteLine($"  Period  : {multiPeriodStart:yyyy-MM-dd} → {multiPeriodEnd:yyyy-MM-dd}");
Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

// ─── Build exchange wrappers ──────────────────────────────────────────────────
var exchanges = new Dictionary<string, IExchangeWrapper>(StringComparer.OrdinalIgnoreCase);
var bitgetSection = configuration.GetSection("ExchangeSettings:Bitget");
if (!string.IsNullOrEmpty(bitgetSection["ApiKey"]))
{
    exchanges["bitget"] = new BitgetWrapper(
        bitgetSection["ApiKey"]!,
        bitgetSection["ApiSecret"]!,
        bitgetSection["Passphrase"]!,
        loggerFactory.CreateLogger<BitgetWrapper>());
    Console.WriteLine("  ✓ Bitget exchange configured.");
}
else
{
    Console.WriteLine("  ℹ  No Bitget API keys found – using synthetic historical data.\n");
}

// ─── Backtest Configuration ───────────────────────────────────────────────────
var config = new BacktestConfig
{
    InitialBalance         = 10_000m,
    TakerFeeRate           = 0.0005m,   // 0.05%
    MakerFeeRate           = 0.0005m,
    SlippageRate           = 0.0001m,
    SizingMode             = PositionSizingMode.PercentageOfPortfolio,
    PositionSize           = 0.95m,
    MaxLossPerTradePercent = 0m
};

var engine   = new BacktestEngine();
var reporter = new BacktestReportGenerator();

Directory.CreateDirectory("./output");

// ─── Per-symbol loop ──────────────────────────────────────────────────────────
foreach (var symbol in symbols)
{
    Console.WriteLine($"\n{new string('═', 62)}");
    Console.WriteLine($"  Backtesting {symbol}");
    Console.WriteLine($"{new string('═', 62)}\n");

    // ── Load / generate candles ────────────────────────────────────────────
    List<OHLCVCandle> candles;

    if (exchanges.Count > 0)
    {
        var dataManager = new HistoricalDataManager(
            exchanges,
            loggerFactory.CreateLogger<HistoricalDataManager>(),
            cacheDirectory: "./backtesting_cache");

        try
        {
            Console.WriteLine($"  Fetching real {symbol} data from Bitget...");
            candles = await dataManager.FetchLongRangeHistoricalDataAsync(
                "bitget", symbol, multiPeriodStart, multiPeriodEnd, "1d");
            Console.WriteLine($"  ✓ {candles.Count} real candles loaded.\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Could not fetch real data: {ex.Message}");
            Console.WriteLine("  ⚠ Falling back to synthetic data...\n");
            candles = GenerateSyntheticMultiPeriod(symbol, multiPeriodStart, multiPeriodEnd);
            Console.WriteLine($"  ✓ {candles.Count} synthetic candles generated.\n");
        }
    }
    else
    {
        Console.WriteLine($"  Generating synthetic {symbol} data...");
        candles = GenerateSyntheticMultiPeriod(symbol, multiPeriodStart, multiPeriodEnd);
        Console.WriteLine($"  ✓ {candles.Count} synthetic candles generated.\n");
    }

    if (candles.Count < 250)
    {
        Console.WriteLine($"  ⚠ Insufficient data for {symbol} (need ≥ 250 candles). Skipping.\n");
        continue;
    }

    // ── Define strategies ──────────────────────────────────────────────────
    var strategies = new List<IBacktestStrategy>
    {
        new BuyAndHoldStrategy(),
        new BtcMacroMaStrategy
        {
            Use200DFilter = false,
            UseTrailing   = true,
            TrailPct      = 5.0,
            TpStepPct     = 10.0,
            TpClosePct    = 20.0,
            MaxSteps      = 5,
            UseScaleIn    = true,
            PullbackPct   = 6.0,
            AddSizePct    = 20.0
        }
    };

    // ── Run backtests via StrategySelector ────────────────────────────────
    Console.WriteLine("  Running backtests for all strategies...\n");
    var results = StrategySelector.EvaluateStrategies(strategies, candles, config.InitialBalance);

    // ── Print results per strategy ─────────────────────────────────────────
    foreach (var (name, result) in results)
    {
        result.Symbol    = symbol;
        result.Timeframe = "1d";
        reporter.PrintSummary(result);

        var safeName = result.StrategyName.Replace(" ", "_").Replace("/", "-").Replace("(", "").Replace(")", "").Replace("&", "and");
        var safeSymbol = symbol.Replace("/", "-");
        reporter.ExportTradesToCsv(result,      $"./output/{safeSymbol}_{safeName}_trades.csv");
        reporter.ExportEquityCurveToCsv(result, $"./output/{safeSymbol}_{safeName}_equity.csv");
        reporter.ExportToJson(result,            $"./output/{safeSymbol}_{safeName}_result.json");
    }

    // ── ML: classify market regime and select best strategy ───────────────
    var regime      = StrategySelector.ClassifyRegime(candles.TakeLast(90).ToList());
    var bestName    = StrategySelector.SelectBestStrategy(results, regime);

    Console.WriteLine($"\n  ┌─────────────────────────────────────────────────────┐");
    Console.WriteLine($"  │  ML Strategy Selection for {symbol,-26}│");
    Console.WriteLine($"  │  Market Regime : {regime,-34}│");
    Console.WriteLine($"  │  Best Strategy : {bestName,-34}│");
    Console.WriteLine($"  └─────────────────────────────────────────────────────┘\n");

    // Quick comparison table
    Console.WriteLine($"  {"Strategy",-32} {"PnL %",8} {"Sharpe",8} {"Win%",8} {"Trades",7}");
    Console.WriteLine($"  {new string('-', 64)}");
    foreach (var (name, result) in results.OrderByDescending(r => r.Value.Metrics.SharpeRatio))
    {
        var m    = result.Metrics;
        var mark = name == bestName ? " ★" : "  ";
        Console.WriteLine($"  {name,-32}{mark} {m.TotalReturnPercentage,6:F1}%  {m.SharpeRatio,6:F2}  {m.WinRate,6:P0}  {m.TotalTrades,5}");
    }
    Console.WriteLine();
}

// Dispose exchange wrappers
foreach (var w in exchanges.Values.OfType<IDisposable>())
    w.Dispose();

Console.WriteLine("\n✓ All backtests complete.");

// ─── Helper: parse CLI arg ────────────────────────────────────────────────────
static string? GetArg(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

// ─── Synthetic Data Generator ─────────────────────────────────────────────────
// Szimulálja a különböző piaci fázisokat: bull (2020-2021), bear (2022),
// recovery (2023), bull (2024), ranging (2025-).
// Az ETH szimulációhoz eltérő seed-et és magasabb volatilitást használ.
static List<OHLCVCandle> GenerateSyntheticMultiPeriod(string symbol, DateTime start, DateTime end)
{
    var candles  = new List<OHLCVCandle>();
    var seed     = symbol.GetHashCode() & 0x7FFFFFFF; // Ensure positive seed by masking sign bit
    var rng      = new Random(seed);
    var current  = start;
    var isEth    = symbol.StartsWith("ETH", StringComparison.OrdinalIgnoreCase);
    var price    = isEth ? 130m : 7000m; // ETH ~130 USD, BTC ~7000 USD (2020 eleje)

    while (current <= end)
    {
        decimal drift, volatility;
        if (current.Year == 2020)      { drift =  0.004m;  volatility = isEth ? 0.040m : 0.030m; }
        else if (current.Year == 2021) { drift =  0.003m;  volatility = isEth ? 0.045m : 0.035m; }
        else if (current.Year == 2022) { drift = -0.003m;  volatility = isEth ? 0.050m : 0.040m; }
        else if (current.Year == 2023) { drift =  0.002m;  volatility = isEth ? 0.030m : 0.025m; }
        else if (current.Year == 2024) { drift =  0.003m;  volatility = isEth ? 0.035m : 0.030m; }
        else                           { drift =  0.0002m; volatility = isEth ? 0.025m : 0.020m; }

        var change     = drift + (decimal)(rng.NextDouble() * (double)volatility * 2 - (double)volatility);
        var open       = price;
        var close      = Math.Max(open * (1m + change), 1m);
        var high       = Math.Max(open, close) * (1m + (decimal)(rng.NextDouble() * 0.008));
        var low        = Math.Min(open, close) * (1m - (decimal)(rng.NextDouble() * 0.008));
        var baseVolume = current.Year <= 2021 ? (isEth ? 8000m : 400m) : current.Year == 2022 ? (isEth ? 10000m : 600m) : (isEth ? 5000m : 300m);
        var volume     = baseVolume + (decimal)(rng.NextDouble() * (double)(baseVolume * 0.5m));

        candles.Add(new OHLCVCandle
        {
            Timestamp = current,
            Open      = Math.Round(open,  2),
            High      = Math.Round(high,  2),
            Low       = Math.Round(low,   2),
            Close     = Math.Round(close, 2),
            Volume    = Math.Round(volume, 4),
            Symbol    = symbol,
            Exchange  = "synthetic"
        });

        price   = close;
        current = current.AddDays(1);
    }
    return candles;
}
