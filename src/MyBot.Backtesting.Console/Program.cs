using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Data;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Optimization;
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
Console.WriteLine("Running Strategy 1/7: SMA Crossover (50/200)...");
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
Console.WriteLine("\nRunning Strategy 2/7: RSI Mean Reversion (period=14, 30/70)...");
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
Console.WriteLine("\nRunning Strategy 3/7: MACD Trend (12/26/9)...");
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

// ─── Strategy 4: Bollinger Bands Breakout ─────────────────────────────────────
Console.WriteLine("\nRunning Strategy 4/7: Bollinger Bands Breakout...");
var bbStrategy = new BollingerBandsStrategy();
bbStrategy.Initialize(new StrategyParameters
{
    ["BollingerPeriod"] = 20,
    ["StdDevMultiplier"] = 2.0m,
    ["StopLossPercent"] = 0.02m,
    ["TakeProfitPercent"] = 0.04m
});
var bbResult = engine.RunBacktest(bbStrategy, candles, config.InitialBalance, config);
bbResult.Symbol = symbol;
bbResult.Timeframe = timeframe;
results.Add(bbResult);
reporter.PrintSummary(bbResult);

// ─── Strategy 5: Triple EMA + RSI ─────────────────────────────────────────────
Console.WriteLine("\nRunning Strategy 5/7: Triple EMA + RSI...");
var tripleEmaStrategy = new TripleEmaRsiStrategy();
tripleEmaStrategy.Initialize(new StrategyParameters
{
    ["FastEmaPeriod"] = 8,
    ["MidEmaPeriod"] = 21,
    ["SlowEmaPeriod"] = 55,
    ["RsiPeriod"] = 14,
    ["TrailingStopPercent"] = 0.05m
});
var tripleEmaResult = engine.RunBacktest(tripleEmaStrategy, candles, config.InitialBalance, config);
tripleEmaResult.Symbol = symbol;
tripleEmaResult.Timeframe = timeframe;
results.Add(tripleEmaResult);
reporter.PrintSummary(tripleEmaResult);

// ─── Strategy 6: Support/Resistance Breakout ──────────────────────────────────
Console.WriteLine("\nRunning Strategy 6/7: Support/Resistance Breakout...");
var srStrategy = new SupportResistanceStrategy();
srStrategy.Initialize(new StrategyParameters
{
    ["LookbackPeriod"] = 50,
    ["BreakoutThreshold"] = 0.005m,
    ["VolumeMultiplier"] = 1.5m,
    ["RiskRewardRatio"] = 2.0m
});
var srResult = engine.RunBacktest(srStrategy, candles, config.InitialBalance, config);
srResult.Symbol = symbol;
srResult.Timeframe = timeframe;
results.Add(srResult);
reporter.PrintSummary(srResult);

// ─── Strategy 7: Volatility Breakout (Turtle) ─────────────────────────────────
Console.WriteLine("\nRunning Strategy 7/7: Volatility Breakout (Donchian/Turtle)...");
var vbStrategy = new VolatilityBreakoutStrategy();
vbStrategy.Initialize(new StrategyParameters
{
    ["ChannelPeriod"] = 20,
    ["AtrPeriod"] = 14,
    ["AtrMultiplier"] = 2.0m
});
var vbResult = engine.RunBacktest(vbStrategy, candles, config.InitialBalance, config);
vbResult.Symbol = symbol;
vbResult.Timeframe = timeframe;
results.Add(vbResult);
reporter.PrintSummary(vbResult);

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

// ─── Parameter Optimization ───────────────────────────────────────────────────
Console.WriteLine("\n\n" + new string('═', 60));
Console.WriteLine("  PARAMETER OPTIMIZATION");
Console.WriteLine(new string('═', 60));

var optimizer = new StrategyOptimizer(engine, loggerFactory.CreateLogger<StrategyOptimizer>());
var bbOptStrategy = new BollingerBandsStrategy();

var paramGrid = new ParameterGrid
{
    ["BollingerPeriod"] = new ParameterRange { Min = 10, Max = 30, Step = 5 },
    ["StdDevMultiplier"] = new ParameterRange { Min = 1.5m, Max = 3.0m, Step = 0.5m },
    ["StopLossPercent"] = new ParameterRange { Min = 0.01m, Max = 0.05m, Step = 0.01m }
};

Console.WriteLine("\nOptimizing Bollinger Bands strategy...");
var optResult = optimizer.OptimizeParameters(bbOptStrategy, candles, paramGrid, config, OptimizationMetric.SharpeRatio);

Console.WriteLine($"\nBest Parameters Found:");
foreach (var param in optResult.BestParameters)
    Console.WriteLine($"  {param.Key}: {param.Value}");

Console.WriteLine($"\nBest Sharpe Ratio: {optResult.BestMetricValue:F3}");
Console.WriteLine($"Total Return:      {optResult.BestBacktestResult.Metrics.TotalReturnPercentage:+0.00;-0.00}%");
Console.WriteLine($"Tested {optResult.TotalCombinationsTested} combinations in {optResult.OptimizationDuration.TotalSeconds:F1}s");

reporter.ExportOptimizationResults(optResult, "./output/bollinger_optimization.csv");

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
