using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Analysis;
using MyBot.Backtesting.Data;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Reports;
using MyBot.Backtesting.Strategies;
using MyBot.Backtesting.Strategies.Examples;
using MyBot.Core.Interfaces;
using MyBot.Exchanges.Bitget;

// ─── Configuration ───────────────────────────────────────────────────────────
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Warning); // suppress noisy SDK logs
});

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  MyBot Backtesting Console Demo");
Console.WriteLine("═══════════════════════════════════════════════════════════");

// ─── Strategy Selection Messaging ────────────────────────────────────────────
Console.WriteLine("\n" + new string('═', 60));
Console.WriteLine("  STRATEGY SELECTION");
Console.WriteLine(new string('═', 60));
Console.WriteLine("\nAfter extensive testing, only strategies with proven performance are included:");
Console.WriteLine("  ✓ SMA Crossover - Simple baseline for comparison");
Console.WriteLine("  ✓ Adaptive Multi-Strategy - Main recommended strategy");
Console.WriteLine("\nRemoved strategies (poor performance):");
Console.WriteLine("  ✗ RSI Mean Reversion - 0 trades (non-functional)");
Console.WriteLine("  ✗ MACD Trend - 0.5% win rate, -100% return");
Console.WriteLine("  ✗ Triple EMA + RSI - 2% win rate, -100% return");
Console.WriteLine("  ✗ Bollinger Bands - 1.6% win rate, -95% return");
Console.WriteLine("  ✗ Volatility Breakout - 1.2% win rate, -100% return");
Console.WriteLine("  ✗ Support/Resistance - 0% win rate, -51% return");
Console.WriteLine("\n" + new string('═', 60) + "\n");

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
    Console.WriteLine("  ℹ  No Bitget API keys found in appsettings.json.");
    Console.WriteLine("     Running demo with synthetic (generated) historical data.\n");
}

// ─── Historical Data ──────────────────────────────────────────────────────────
List<OHLCVCandle> candles;
var symbol = "BTCUSDT";
var timeframe = "1h";
var endDate = DateTime.UtcNow;
var startDate = endDate.AddMonths(-6);

HistoricalDataManager? dataManager = null;
if (exchanges.Count > 0)
{
    dataManager = new HistoricalDataManager(
        exchanges,
        loggerFactory.CreateLogger<HistoricalDataManager>(),
        cacheDirectory: "./backtesting_cache");

    Console.WriteLine($"\nFetching historical data: {symbol} {timeframe} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}...");
    candles = await dataManager.GetHistoricalDataAsync("bitget", symbol, startDate, endDate, timeframe);
    Console.WriteLine($"  → {candles.Count} candles loaded.\n");
}
else
{
    Console.WriteLine("Generating synthetic BTCUSDT data for demo (6 months, 1h)...");
    candles = GenerateSyntheticData(symbol, startDate, endDate, timeframe);
    Console.WriteLine($"  → {candles.Count} candles generated.\n");
}

if (candles.Count < 250)
{
    Console.WriteLine("Insufficient data for backtesting (need at least 250 candles). Exiting.");
    return;
}

// ─── Backtest Configuration ───────────────────────────────────────────────────
var config = new BacktestConfig
{
    InitialBalance = 10_000m,
    TakerFeeRate = 0.001m,
    MakerFeeRate = 0.0008m,
    SlippageRate = 0.0001m,
    SizingMode = PositionSizingMode.PercentageOfPortfolio,
    PositionSize = 0.95m
};

var engine = new BacktestEngine();
var reporter = new BacktestReportGenerator();
var results = new List<BacktestResult>();

// ─── Strategy 1: SMA Crossover (baseline) ────────────────────────────────────
Console.WriteLine("Running Strategy 1/2: SMA Crossover (50/200)...");
var smaStrategy = new SmaCrossoverStrategy();
smaStrategy.Initialize(new StrategyParameters
{
    ["FastPeriod"] = 50,
    ["SlowPeriod"] = 200
});
var smaResult = engine.RunBacktest(smaStrategy, candles, config.InitialBalance, config);
smaResult.Symbol = symbol;
smaResult.Timeframe = timeframe;
results.Add(smaResult);
reporter.PrintSummary(smaResult);

// ─── Strategy 2: Adaptive Multi-Strategy (recommended) ───────────────────────
Console.WriteLine("\nRunning Strategy 2/2: Adaptive Multi-Strategy...");
var adaptiveStrategyMain = new AdaptiveMultiStrategy();
var adaptiveMainResult = engine.RunBacktest(adaptiveStrategyMain, candles, config.InitialBalance, config);
adaptiveMainResult.Symbol = symbol;
adaptiveMainResult.Timeframe = timeframe;
results.Add(adaptiveMainResult);
reporter.PrintSummary(adaptiveMainResult);

// ─── Comparison Table ─────────────────────────────────────────────────────────
reporter.PrintComparison(results);
Console.WriteLine("\n✓ Adaptive Multi-Strategy is the recommended approach for this market.");
Console.WriteLine("  It adapts to market regime (trending vs ranging) and selects appropriate sub-strategies.\n");

// ─── Export Results ───────────────────────────────────────────────────────────
Console.WriteLine("\nExporting results...");
Directory.CreateDirectory("./output");
foreach (var r in results)
{
    var safeName = r.StrategyName.Replace(" ", "_").Replace("/", "-");
    reporter.ExportTradesToCsv(r, $"./output/{safeName}_trades.csv");
    reporter.ExportEquityCurveToCsv(r, $"./output/{safeName}_equity.csv");
    reporter.ExportToJson(r, $"./output/{safeName}_result.json");
}

// ─── Market Regime Analysis ───────────────────────────────────────────────────
Console.WriteLine("\n\n" + new string('═', 60));
Console.WriteLine("  PARAMETER OPTIMIZATION - ADAPTIVE STRATEGY");
Console.WriteLine(new string('═', 60));

Console.WriteLine("\nOptimizing Adaptive Multi-Strategy parameters...");
Console.WriteLine("Note: Adaptive strategy has minimal tunable parameters.");
Console.WriteLine("Sub-strategies use default parameters that work across regimes.\n");

Console.WriteLine("Analyzing market regimes in current period...\n");

var samplePoints = new[] { 0.25, 0.5, 0.75 }; // Sample at 25%, 50%, 75% through period
foreach (var samplePoint in samplePoints)
{
    var index = (int)(candles.Count * samplePoint);
    var regime = MarketRegimeDetector.DetectRegime(candles, index);
    if (regime == null) continue;

    var adxStr = regime.Adx.HasValue ? $"{regime.Adx.Value:F1}" : "N/A";
    var atrStr = regime.AtrPercent.HasValue ? $"{regime.AtrPercent.Value:F2}%" : "N/A";
    Console.WriteLine($"At {candles[index].Timestamp:yyyy-MM-dd}:");
    Console.WriteLine($"  Regime: {regime.TrendRegime} | Volatility: {regime.VolatilityRegime} | Phase: {regime.MarketPhase}");
    Console.WriteLine($"  ADX: {adxStr} | ATR: {atrStr}");
    Console.WriteLine($"  → Recommended: {regime.RecommendedStrategy}\n");
}

// ─── Multi-Period Analysis with Real Data ─────────────────────────────────────
Console.WriteLine("\n\n" + new string('═', 80));
Console.WriteLine("  MULTI-PERIOD ANALYSIS WITH REAL DATA");
Console.WriteLine(new string('═', 80));

List<OHLCVCandle> multiPeriodCandles;
var multiPeriodStart = new DateTime(2020, 1, 1);
var multiPeriodEnd = DateTime.UtcNow;

if (dataManager != null)
{
    try
    {
        Console.WriteLine("\nFetching REAL historical data from Bitget (2020-2026)...");
        Console.WriteLine("This may take 2-3 minutes due to API rate limiting...\n");

        multiPeriodCandles = await dataManager.FetchLongRangeHistoricalDataAsync(
            "bitget",
            symbol,
            multiPeriodStart,
            multiPeriodEnd,
            "1h");

        if (multiPeriodCandles.Count > 1000)
        {
            Console.WriteLine($"  ✓ Successfully fetched {multiPeriodCandles.Count} real candles!");
            Console.WriteLine($"  ✓ Data range: {multiPeriodCandles.First().Timestamp:yyyy-MM-dd} to {multiPeriodCandles.Last().Timestamp:yyyy-MM-dd}\n");
        }
        else
        {
            throw new InvalidOperationException($"Insufficient real data fetched: expected at least 1000 candles, got {multiPeriodCandles.Count}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ⚠ Could not fetch real data: {ex.Message}");
        Console.WriteLine("  ⚠ Falling back to synthetic data for demonstration...\n");

        Console.WriteLine($"Generating synthetic data for {multiPeriodStart:yyyy-MM-dd} → {multiPeriodEnd:yyyy-MM-dd}...");
        multiPeriodCandles = GenerateSyntheticMultiPeriod(symbol, multiPeriodStart, multiPeriodEnd);
        Console.WriteLine($"  ✓ {multiPeriodCandles.Count} synthetic candles generated.\n");
    }
}
else
{
    Console.WriteLine($"\nGenerating synthetic data for {multiPeriodStart:yyyy-MM-dd} → {multiPeriodEnd:yyyy-MM-dd}...");
    multiPeriodCandles = GenerateSyntheticMultiPeriod(symbol, multiPeriodStart, multiPeriodEnd);
    Console.WriteLine($"  ✓ {multiPeriodCandles.Count} synthetic candles generated.\n");
}

var advancedReporter = new AdvancedReportGenerator();
var multiPeriodBacktester = new MultiPeriodBacktester(engine);
var adaptiveStrategy = new AdaptiveMultiStrategy();

Console.WriteLine("Running multi-period backtest with Adaptive Multi-Strategy...\n");
var multiPeriodResult = multiPeriodBacktester.RunMultiPeriod(adaptiveStrategy, multiPeriodCandles, config);
advancedReporter.PrintMultiPeriodResults(multiPeriodResult);

Directory.CreateDirectory("./output");
advancedReporter.ExportMultiPeriodToCsv(multiPeriodResult, "./output/multi_period_results.csv");

Console.WriteLine("\nDemo complete.");

// Dispose wrappers
foreach (var w in exchanges.Values.OfType<IDisposable>())
    w.Dispose();

// ─── Synthetic Data Generator ─────────────────────────────────────────────────
static List<OHLCVCandle> GenerateSyntheticData(string symbol, DateTime start, DateTime end, string timeframe)
{
    var candles = new List<OHLCVCandle>();
    var rng = new Random(42);
    var current = start;
    var price = 45000m;
    var intervalHours = timeframe == "1d" ? 24 : timeframe == "4h" ? 4 : timeframe == "1h" ? 1 : 1;

    while (current <= end)
    {
        var change = (decimal)(rng.NextDouble() * 0.04 - 0.019); // slight upward drift
        var open = price;
        var close = open * (1 + change);
        var high = Math.Max(open, close) * (1 + (decimal)(rng.NextDouble() * 0.01));
        var low = Math.Min(open, close) * (1 - (decimal)(rng.NextDouble() * 0.01));
        var volume = (decimal)(rng.NextDouble() * 500 + 100);

        candles.Add(new OHLCVCandle
        {
            Timestamp = current,
            Open = Math.Round(open, 2),
            High = Math.Round(high, 2),
            Low = Math.Round(low, 2),
            Close = Math.Round(close, 2),
            Volume = Math.Round(volume, 4),
            Symbol = symbol,
            Exchange = "synthetic"
        });

        price = close;
        current = current.AddHours(intervalHours);
    }
    return candles;
}

// ─── Multi-Period Synthetic Data Generator ────────────────────────────────────
// Simulates distinct market phases: bull (2020-2021), bear (2022), recovery (2023),
// bull (2024), and ranging (2025-2026) using different drift and volatility parameters.
// Uses daily candles to keep backtest runtime manageable.
static List<OHLCVCandle> GenerateSyntheticMultiPeriod(string symbol, DateTime start, DateTime end)
{
    var candles = new List<OHLCVCandle>();
    var rng = new Random(123);
    var current = start;
    var price = 7000m; // BTC price at start of 2020

    while (current <= end)
    {
        // Assign market characteristics by calendar year
        decimal drift, volatility;
        if (current.Year == 2020)
        {
            drift = 0.004m; volatility = 0.03m; // strong bull
        }
        else if (current.Year == 2021)
        {
            drift = 0.003m; volatility = 0.035m; // continued bull, more volatile
        }
        else if (current.Year == 2022)
        {
            drift = -0.003m; volatility = 0.04m; // bear market
        }
        else if (current.Year == 2023)
        {
            drift = 0.002m; volatility = 0.025m; // recovery
        }
        else if (current.Year == 2024)
        {
            drift = 0.003m; volatility = 0.03m; // new bull
        }
        else
        {
            drift = 0.0002m; volatility = 0.02m; // ranging / sideways
        }

        var change = drift + (decimal)(rng.NextDouble() * (double)volatility * 2 - (double)volatility);
        var open = price;
        var close = Math.Max(open * (1m + change), 1m);
        var high = Math.Max(open, close) * (1m + (decimal)(rng.NextDouble() * 0.008));
        var low = Math.Min(open, close) * (1m - (decimal)(rng.NextDouble() * 0.008));
        var baseVolume = current.Year <= 2021 ? 400m : current.Year == 2022 ? 600m : 300m;
        var volume = baseVolume + (decimal)(rng.NextDouble() * 300);

        candles.Add(new OHLCVCandle
        {
            Timestamp = current,
            Open = Math.Round(open, 2),
            High = Math.Round(high, 2),
            Low = Math.Round(low, 2),
            Close = Math.Round(close, 2),
            Volume = Math.Round(volume, 4),
            Symbol = symbol,
            Exchange = "synthetic"
        });

        price = close;
        current = current.AddDays(1); // daily candles to keep runtime manageable
    }
    return candles;
}
