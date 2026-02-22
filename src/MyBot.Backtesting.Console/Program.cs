using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Data;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Reports;
using MyBot.Backtesting.Strategies;
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
Console.WriteLine("  MyBot Backtesting Console – BTC Macro MA Trend Strategy");
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
    Console.WriteLine("  ℹ  No Bitget API keys found – using synthetic historical data.\n");
}

// ─── Historical Data ──────────────────────────────────────────────────────────
// A BtcMacroMaStrategy 200 napi SMA-t és 21 heti EMA-t használ,
// ezért legalább 2020-tól kell az adat (napi gyertyák).
var symbol           = "BTCUSDT";
var multiPeriodStart = new DateTime(2020, 1, 1);
var multiPeriodEnd   = DateTime.UtcNow;

List<OHLCVCandle> candles;

if (exchanges.Count > 0)
{
    var dataManager = new HistoricalDataManager(
        exchanges,
        loggerFactory.CreateLogger<HistoricalDataManager>(),
        cacheDirectory: "./backtesting_cache");

    try
    {
        Console.WriteLine($"\nFetching real BTC data from Bitget ({multiPeriodStart:yyyy-MM-dd} → {multiPeriodEnd:yyyy-MM-dd})...");
        Console.WriteLine("This may take 2-3 minutes due to API rate limiting...\n");

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
    Console.WriteLine($"Generating synthetic BTC/USD data ({multiPeriodStart:yyyy-MM-dd} → {multiPeriodEnd:yyyy-MM-dd})...");
    candles = GenerateSyntheticMultiPeriod(symbol, multiPeriodStart, multiPeriodEnd);
    Console.WriteLine($"  ✓ {candles.Count} synthetic candles generated.\n");
}

if (candles.Count < 250)
{
    Console.WriteLine("Insufficient data for backtesting (need at least 250 candles). Exiting.");
    return;
}

// ─── Backtest Configuration ───────────────────────────────────────────────────
var config = new BacktestConfig
{
    InitialBalance       = 10_000m,
    TakerFeeRate         = 0.0005m,   // 0.05% (tipikus spot fee)
    MakerFeeRate         = 0.0005m,
    SlippageRate         = 0.0001m,
    SizingMode           = PositionSizingMode.PercentageOfPortfolio,
    PositionSize         = 0.95m,
    MaxLossPerTradePercent = 0m       // A stratégia saját trailing stopot használ
};

var engine   = new BacktestEngine();
var reporter = new BacktestReportGenerator();

// ─── BtcMacroMaStrategy ───────────────────────────────────────────────────────
Console.WriteLine("Running BtcMacroMaStrategy (Bull Market Support Band)...\n");

var btcStrategy = new BtcMacroMaStrategy
{
    Use200DFilter = false,
    UseTrailing   = true,
    TrailPct      = 5.0
};

var btcResult = engine.RunBacktest(btcStrategy, candles, config.InitialBalance, config);
btcResult.Symbol    = symbol;
btcResult.Timeframe = "1d";

reporter.PrintSummary(btcResult);

// ─── Export Results ───────────────────────────────────────────────────────────
Console.WriteLine("\nExporting results...");
Directory.CreateDirectory("./output");
var safeName = btcResult.StrategyName.Replace(" ", "_").Replace("/", "-").Replace("(", "").Replace(")", "");
reporter.ExportTradesToCsv(btcResult,    $"./output/{safeName}_trades.csv");
reporter.ExportEquityCurveToCsv(btcResult, $"./output/{safeName}_equity.csv");
reporter.ExportToJson(btcResult,          $"./output/{safeName}_result.json");

Console.WriteLine("\n✓ Backtest complete.");

// Dispose wrappers
foreach (var w in exchanges.Values.OfType<IDisposable>())
    w.Dispose();

// ─── Synthetic Data Generator ─────────────────────────────────────────────────
// Szimulálja a BTC különböző piaci fázisait: bull (2020-2021), bear (2022),
// recovery (2023), bull (2024), ranging (2025-2026).
// Napi gyertyákat generál (a stratégia 200D SMA-hoz elegendő adatot igényel).
static List<OHLCVCandle> GenerateSyntheticMultiPeriod(string symbol, DateTime start, DateTime end)
{
    var candles = new List<OHLCVCandle>();
    var rng     = new Random(123);
    var current = start;
    var price   = 7000m; // BTC ár 2020 elején

    while (current <= end)
    {
        // Piaci jellemzők évente
        decimal drift, volatility;
        if (current.Year == 2020)
        {
            drift = 0.004m; volatility = 0.03m;   // erős bull
        }
        else if (current.Year == 2021)
        {
            drift = 0.003m; volatility = 0.035m;  // folytatódó bull, magasabb vol
        }
        else if (current.Year == 2022)
        {
            drift = -0.003m; volatility = 0.04m;  // bear piac
        }
        else if (current.Year == 2023)
        {
            drift = 0.002m; volatility = 0.025m;  // recovery
        }
        else if (current.Year == 2024)
        {
            drift = 0.003m; volatility = 0.03m;   // új bull
        }
        else
        {
            drift = 0.0002m; volatility = 0.02m;  // ranging / sideways
        }

        var change = drift + (decimal)(rng.NextDouble() * (double)volatility * 2 - (double)volatility);
        var open   = price;
        var close  = Math.Max(open * (1m + change), 1m);
        var high   = Math.Max(open, close) * (1m + (decimal)(rng.NextDouble() * 0.008));
        var low    = Math.Min(open, close) * (1m - (decimal)(rng.NextDouble() * 0.008));
        var baseVolume = current.Year <= 2021 ? 400m : current.Year == 2022 ? 600m : 300m;
        var volume = baseVolume + (decimal)(rng.NextDouble() * 300);

        candles.Add(new OHLCVCandle
        {
            Timestamp = current,
            Open      = Math.Round(open, 2),
            High      = Math.Round(high, 2),
            Low       = Math.Round(low, 2),
            Close     = Math.Round(close, 2),
            Volume    = Math.Round(volume, 4),
            Symbol    = symbol,
            Exchange  = "synthetic"
        });

        price   = close;
        current = current.AddDays(1); // napi gyertyák
    }
    return candles;
}
