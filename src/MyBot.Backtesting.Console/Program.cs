using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

if (exchanges.Count > 0)
{
    var dataManager = new HistoricalDataManager(
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

// ─── Strategy 1: SMA Crossover ────────────────────────────────────────────────
Console.WriteLine("Running Strategy 1/3: SMA Crossover (50/200)...");
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

// ─── Strategy 2: RSI Mean Reversion ──────────────────────────────────────────
Console.WriteLine("\nRunning Strategy 2/3: RSI Mean Reversion (period=14, 30/70)...");
var rsiStrategy = new RsiMeanReversionStrategy();
rsiStrategy.Initialize(new StrategyParameters
{
    ["RsiPeriod"] = 14,
    ["Oversold"] = 30m,
    ["Overbought"] = 70m
});
var rsiResult = engine.RunBacktest(rsiStrategy, candles, config.InitialBalance, config);
rsiResult.Symbol = symbol;
rsiResult.Timeframe = timeframe;
results.Add(rsiResult);
reporter.PrintSummary(rsiResult);

// ─── Strategy 3: MACD Trend ───────────────────────────────────────────────────
Console.WriteLine("\nRunning Strategy 3/3: MACD Trend (12/26/9)...");
var macdStrategy = new MacdTrendStrategy();
macdStrategy.Initialize(new StrategyParameters
{
    ["FastPeriod"] = 12,
    ["SlowPeriod"] = 26,
    ["SignalPeriod"] = 9
});
var macdResult = engine.RunBacktest(macdStrategy, candles, config.InitialBalance, config);
macdResult.Symbol = symbol;
macdResult.Timeframe = timeframe;
results.Add(macdResult);
reporter.PrintSummary(macdResult);

// ─── Comparison Table ─────────────────────────────────────────────────────────
reporter.PrintComparison(results);

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
