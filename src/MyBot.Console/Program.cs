using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyBot.Core.Interfaces;
using MyBot.Exchanges.Bitget;
using MyBot.Exchanges.BingX;
using MyBot.Exchanges.Mexc;
using MyBot.Exchanges.Bybit;

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

// Build DI container
var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddSingleton<IConfiguration>(configuration);

var serviceProvider = services.BuildServiceProvider();
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

// Build exchange wrappers from configuration
var exchangeSettings = configuration.GetSection("ExchangeSettings");
var wrappers = new List<IExchangeWrapper>();

var bitgetSection = exchangeSettings.GetSection("Bitget");
if (!string.IsNullOrEmpty(bitgetSection["ApiKey"]))
{
    wrappers.Add(new BitgetWrapper(
        bitgetSection["ApiKey"]!,
        bitgetSection["ApiSecret"]!,
        bitgetSection["Passphrase"]!,
        loggerFactory.CreateLogger<BitgetWrapper>()));
}

var bingxSection = exchangeSettings.GetSection("BingX");
if (!string.IsNullOrEmpty(bingxSection["ApiKey"]))
{
    wrappers.Add(new BingXWrapper(
        bingxSection["ApiKey"]!,
        bingxSection["ApiSecret"]!,
        loggerFactory.CreateLogger<BingXWrapper>()));
}

var mexcSection = exchangeSettings.GetSection("Mexc");
if (!string.IsNullOrEmpty(mexcSection["ApiKey"]))
{
    wrappers.Add(new MexcWrapper(
        mexcSection["ApiKey"]!,
        mexcSection["ApiSecret"]!,
        loggerFactory.CreateLogger<MexcWrapper>()));
}

var bybitSection = exchangeSettings.GetSection("Bybit");
if (!string.IsNullOrEmpty(bybitSection["ApiKey"]))
{
    wrappers.Add(new BybitWrapper(
        bybitSection["ApiKey"]!,
        bybitSection["ApiSecret"]!,
        loggerFactory.CreateLogger<BybitWrapper>()));
}

if (wrappers.Count == 0)
{
    Console.WriteLine("No API keys configured. Add your API keys to appsettings.json to get started.");
    Console.WriteLine("\nSupported exchanges:");
    Console.WriteLine("  - Bitget  (ApiKey, ApiSecret, Passphrase)");
    Console.WriteLine("  - BingX   (ApiKey, ApiSecret)");
    Console.WriteLine("  - MEXC    (ApiKey, ApiSecret)");
    Console.WriteLine("  - Bybit   (ApiKey, ApiSecret)");
    Console.WriteLine("\nExample usage (once API keys are configured):");
    Console.WriteLine("  GET TICKER:  wrapper.GetTickerAsync(\"BTCUSDT\")");
    Console.WriteLine("  GET ORDERS:  wrapper.GetOpenOrdersAsync()");
    Console.WriteLine("  GET BALANCE: wrapper.GetBalancesAsync()");
    return;
}

// Demo: fetch public market data (no API key needed)
Console.WriteLine("MyBot - Cryptocurrency Exchange Wrapper Demo");
Console.WriteLine("============================================\n");

var symbol = "BTCUSDT";
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

foreach (var wrapper in wrappers)
{
    Console.WriteLine($"\n--- {wrapper.ExchangeName} ---");
    try
    {
        // Ticker
        var ticker = await wrapper.GetTickerAsync(symbol, cts.Token);
        Console.WriteLine($"  {symbol} Price: {ticker.LastPrice:F2} | Bid: {ticker.BidPrice:F2} | Ask: {ticker.AskPrice:F2}");
        Console.WriteLine($"  24h Change: {ticker.ChangePercent24h:F2}% | High: {ticker.High24h:F2} | Low: {ticker.Low24h:F2}");

        // Order Book (top 3 levels)
        var book = await wrapper.GetOrderBookAsync(symbol, 3, cts.Token);
        Console.WriteLine($"  Order Book top bids: {string.Join(", ", book.Bids.Take(3).Select(b => $"{b.Price:F2}@{b.Quantity:F4}"))}");
        Console.WriteLine($"  Order Book top asks: {string.Join(", ", book.Asks.Take(3).Select(a => $"{a.Price:F2}@{a.Quantity:F4}"))}");

        // Balances (requires auth)
        var balances = await wrapper.GetBalancesAsync(cts.Token);
        var nonZero = balances.Where(b => b.Total > 0).ToList();
        if (nonZero.Any())
        {
            Console.WriteLine("  Balances:");
            foreach (var b in nonZero.Take(5))
                Console.WriteLine($"    {b.Asset}: {b.Available:F8} available, {b.Locked:F8} locked");
        }
        else
        {
            Console.WriteLine("  Balances: (empty or no assets)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error: {ex.Message}");
    }
}

Console.WriteLine("\nDemo complete.");

// Dispose wrappers
foreach (var wrapper in wrappers.OfType<IDisposable>())
    wrapper.Dispose();
