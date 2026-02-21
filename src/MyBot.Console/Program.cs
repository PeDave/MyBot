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
var wsClients = new List<IExchangeWebSocketClient>();

var bitgetSection = exchangeSettings.GetSection("Bitget");
if (!string.IsNullOrEmpty(bitgetSection["ApiKey"]))
{
    wrappers.Add(new BitgetWrapper(
        bitgetSection["ApiKey"]!,
        bitgetSection["ApiSecret"]!,
        bitgetSection["Passphrase"]!,
        loggerFactory.CreateLogger<BitgetWrapper>()));
    wsClients.Add(new BitgetWebSocketClient(
        bitgetSection["ApiKey"]!,
        bitgetSection["ApiSecret"]!,
        bitgetSection["Passphrase"]!,
        loggerFactory.CreateLogger<BitgetWebSocketClient>()));
}

var bingxSection = exchangeSettings.GetSection("BingX");
if (!string.IsNullOrEmpty(bingxSection["ApiKey"]))
{
    wrappers.Add(new BingXWrapper(
        bingxSection["ApiKey"]!,
        bingxSection["ApiSecret"]!,
        loggerFactory.CreateLogger<BingXWrapper>()));
    wsClients.Add(new BingXWebSocketClient(
        bingxSection["ApiKey"]!,
        bingxSection["ApiSecret"]!,
        loggerFactory.CreateLogger<BingXWebSocketClient>()));
}

var mexcSection = exchangeSettings.GetSection("Mexc");
if (!string.IsNullOrEmpty(mexcSection["ApiKey"]))
{
    wrappers.Add(new MexcWrapper(
        mexcSection["ApiKey"]!,
        mexcSection["ApiSecret"]!,
        loggerFactory.CreateLogger<MexcWrapper>()));
    wsClients.Add(new MexcWebSocketClient(
        mexcSection["ApiKey"]!,
        mexcSection["ApiSecret"]!,
        loggerFactory.CreateLogger<MexcWebSocketClient>()));
}

var bybitSection = exchangeSettings.GetSection("Bybit");
if (!string.IsNullOrEmpty(bybitSection["ApiKey"]))
{
    wrappers.Add(new BybitWrapper(
        bybitSection["ApiKey"]!,
        bybitSection["ApiSecret"]!,
        loggerFactory.CreateLogger<BybitWrapper>()));
    wsClients.Add(new BybitWebSocketClient(
        bybitSection["ApiKey"]!,
        bybitSection["ApiSecret"]!,
        loggerFactory.CreateLogger<BybitWebSocketClient>()));
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
    Console.WriteLine("\nWebSocket example usage:");
    Console.WriteLine("  SUBSCRIBE TICKER:    wsClient.SubscribeToTickerAsync(\"BTCUSDT\")");
    Console.WriteLine("  SUBSCRIBE ORDERBOOK: wsClient.SubscribeToOrderBookAsync(\"BTCUSDT\")");
    Console.WriteLine("  SUBSCRIBE TRADES:    wsClient.SubscribeToTradesAsync(\"BTCUSDT\")");
    Console.WriteLine("  SUBSCRIBE ORDERS:    wsClient.SubscribeToUserOrdersAsync()");
    Console.WriteLine("  SUBSCRIBE BALANCE:   wsClient.SubscribeToUserBalanceAsync()");
    return;
}

// Demo: fetch public market data via REST API
Console.WriteLine("MyBot - Cryptocurrency Exchange Wrapper Demo");
Console.WriteLine("============================================\n");

var symbol = "BTCUSDT";
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

foreach (var wrapper in wrappers)
{
    Console.WriteLine($"\n--- {wrapper.ExchangeName} (REST) ---");
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

// Demo: WebSocket real-time data
Console.WriteLine("\n\nMyBot - WebSocket Demo");
Console.WriteLine("======================\n");
Console.WriteLine($"Subscribing to {symbol} streams for 10 seconds...\n");

using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

foreach (var wsClient in wsClients)
{
    Console.WriteLine($"--- {wsClient.ExchangeName} (WebSocket) ---");

    // Attach event handlers
    wsClient.OnTickerUpdate += (_, update) =>
        Console.WriteLine($"  [{update.Exchange}] TICKER {update.Symbol}: {update.LastPrice:F2} ({update.ChangePercent24h:F2}%)");

    wsClient.OnOrderBookUpdate += (_, update) =>
    {
        var topBid = update.Bids.FirstOrDefault();
        var topAsk = update.Asks.FirstOrDefault();
        Console.WriteLine($"  [{update.Exchange}] ORDERBOOK {update.Symbol}: Bid={topBid?.Price:F2} Ask={topAsk?.Price:F2}");
    };

    wsClient.OnTradeUpdate += (_, update) =>
        Console.WriteLine($"  [{update.Exchange}] TRADE {update.Symbol}: {update.Price:F2} x {update.Quantity:F6} ({update.TakerSide})");

    wsClient.OnOrderUpdate += (_, update) =>
        Console.WriteLine($"  [{update.Exchange}] ORDER {update.OrderId}: {update.Symbol} {update.Side} {update.Status}");

    wsClient.OnBalanceUpdate += (_, update) =>
        Console.WriteLine($"  [{update.Exchange}] BALANCE {update.Asset}: {update.Available:F8} available");

    wsClient.OnError += (_, error) =>
        Console.WriteLine($"  [{wsClient.ExchangeName}] ERROR: {error}");

    try
    {
        // Subscribe to public streams
        await wsClient.SubscribeToTickerAsync(symbol, wsCts.Token);
        Console.WriteLine($"  Subscribed to ticker for {symbol}");

        await wsClient.SubscribeToOrderBookAsync(symbol, 5, wsCts.Token);
        Console.WriteLine($"  Subscribed to order book for {symbol}");

        await wsClient.SubscribeToTradesAsync(symbol, wsCts.Token);
        Console.WriteLine($"  Subscribed to trades for {symbol}");

        // Subscribe to private streams (only if authenticated)
        try
        {
            await wsClient.SubscribeToUserOrdersAsync(wsCts.Token);
            Console.WriteLine("  Subscribed to user orders");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Skipping user orders: {ex.Message}");
        }

        try
        {
            await wsClient.SubscribeToUserBalanceAsync(wsCts.Token);
            Console.WriteLine("  Subscribed to user balance");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Skipping user balance: {ex.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error during subscription: {ex.Message}");
    }
}

// Wait for WebSocket data
Console.WriteLine("\nReceiving real-time data for 10 seconds (press Ctrl+C to stop)...\n");
await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);

// Disconnect all WebSocket clients
Console.WriteLine("\nDisconnecting WebSocket clients...");
foreach (var wsClient in wsClients)
{
    try
    {
        await wsClient.DisconnectAsync();
        Console.WriteLine($"  {wsClient.ExchangeName}: disconnected");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {wsClient.ExchangeName}: error during disconnect - {ex.Message}");
    }
}

Console.WriteLine("\nDemo complete.");

// Dispose wrappers and WebSocket clients
foreach (var wrapper in wrappers.OfType<IDisposable>())
    wrapper.Dispose();
foreach (var wsClient in wsClients)
    wsClient.Dispose();

