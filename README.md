# MyBot - Cryptocurrency Exchange Wrapper Library

A unified C# wrapper/bridge library for multiple cryptocurrency exchanges, providing a single consistent API interface across Bitget, BingX, MEXC, and Bybit.

## Project Structure

```
MyBot/
├── MyBot.slnx                    # Solution file
├── src/
│   ├── MyBot.Core/               # Core interfaces, models, and exceptions
│   │   ├── Interfaces/
│   │   │   └── IExchangeWrapper.cs   # Unified exchange interface
│   │   ├── Models/
│   │   │   ├── Enums.cs              # OrderSide, OrderType, OrderStatus, TimeInForce
│   │   │   ├── UnifiedBalance.cs     # Account balance model
│   │   │   ├── UnifiedOrder.cs       # Order model
│   │   │   ├── UnifiedTicker.cs      # Ticker/price model
│   │   │   ├── UnifiedOrderBook.cs   # Order book model
│   │   │   └── UnifiedTrade.cs       # Trade model
│   │   └── Exceptions/
│   │       └── ExchangeException.cs  # Exchange-specific exceptions
│   ├── MyBot.Exchanges/          # Exchange wrapper implementations
│   │   ├── Bitget/BitgetWrapper.cs   # Bitget exchange (JK.Bitget.Net)
│   │   ├── BingX/BingXWrapper.cs     # BingX exchange (JK.BingX.Net)
│   │   ├── Mexc/MexcWrapper.cs       # MEXC exchange (JK.Mexc.Net)
│   │   └── Bybit/BybitWrapper.cs     # Bybit exchange (Bybit.Net)
│   └── MyBot.Console/            # Console demo application
│       ├── Program.cs
│       └── appsettings.json
└── README.md
```

## Supported Exchanges

| Exchange | SDK Package      | Version |
|----------|------------------|---------|
| Bitget   | JK.Bitget.Net    | 3.*     |
| BingX    | JK.BingX.Net     | 3.*     |
| MEXC     | JK.Mexc.Net      | 4.*     |
| Bybit    | Bybit.Net        | 6.*     |

## Unified API

All exchange wrappers implement `IExchangeWrapper`:

```csharp
public interface IExchangeWrapper
{
    string ExchangeName { get; }

    Task<IEnumerable<UnifiedBalance>> GetBalancesAsync(CancellationToken ct = default);

    Task<UnifiedOrder> PlaceOrderAsync(string symbol, OrderSide side, OrderType type,
        decimal quantity, decimal? price = null,
        TimeInForce tif = TimeInForce.GoodTillCanceled, CancellationToken ct = default);

    Task<UnifiedOrder> CancelOrderAsync(string symbol, string orderId, CancellationToken ct = default);
    Task<UnifiedOrder> GetOrderAsync(string symbol, string orderId, CancellationToken ct = default);
    Task<IEnumerable<UnifiedOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken ct = default);
    Task<IEnumerable<UnifiedOrder>> GetOrderHistoryAsync(string? symbol = null, int limit = 50, CancellationToken ct = default);
    Task<UnifiedTicker> GetTickerAsync(string symbol, CancellationToken ct = default);
    Task<UnifiedOrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default);
    Task<IEnumerable<UnifiedTrade>> GetRecentTradesAsync(string symbol, int limit = 50, CancellationToken ct = default);
}
```

## Quick Start

### 1. Configure API Keys

Edit `src/MyBot.Console/appsettings.json`:

```json
{
  "ExchangeSettings": {
    "Bitget": {
      "ApiKey": "your-api-key",
      "ApiSecret": "your-api-secret",
      "Passphrase": "your-passphrase"
    },
    "BingX": {
      "ApiKey": "your-api-key",
      "ApiSecret": "your-api-secret"
    },
    "Mexc": {
      "ApiKey": "your-api-key",
      "ApiSecret": "your-api-secret"
    },
    "Bybit": {
      "ApiKey": "your-api-key",
      "ApiSecret": "your-api-secret"
    }
  }
}
```

### 2. Use in Your Code

```csharp
using Microsoft.Extensions.Logging;
using MyBot.Exchanges.Bybit;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
using var bybit = new BybitWrapper("apiKey", "apiSecret",
    loggerFactory.CreateLogger<BybitWrapper>());

// Get ticker
var ticker = await bybit.GetTickerAsync("BTCUSDT");
Console.WriteLine($"BTC Price: {ticker.LastPrice}");

// Get account balances
var balances = await bybit.GetBalancesAsync();
foreach (var b in balances.Where(b => b.Total > 0))
    Console.WriteLine($"{b.Asset}: {b.Available} available");

// Place a limit buy order
var order = await bybit.PlaceOrderAsync(
    symbol: "BTCUSDT",
    side: OrderSide.Buy,
    type: OrderType.Limit,
    quantity: 0.001m,
    price: 50000m);
Console.WriteLine($"Order placed: {order.OrderId}");

// Cancel the order
var cancelled = await bybit.CancelOrderAsync("BTCUSDT", order.OrderId);

// Get order book
var book = await bybit.GetOrderBookAsync("BTCUSDT", depth: 10);
Console.WriteLine($"Best bid: {book.Bids[0].Price}, Best ask: {book.Asks[0].Price}");
```

### 3. Run the Demo

```bash
cd src/MyBot.Console
dotnet run
```

## Building

```bash
dotnet restore
dotnet build
```

## Requirements

- .NET 8.0 SDK or later
- API keys from the exchange(s) you want to use

## Error Handling

All wrappers throw typed exceptions from `MyBot.Core.Exceptions`:

- `ExchangeException` — base class for all exchange errors
- `ExchangeAuthenticationException` — API key/authentication failures
- `ExchangeRateLimitException` — rate limit exceeded (includes `RetryAfter`)

```csharp
try
{
    var ticker = await wrapper.GetTickerAsync("BTCUSDT");
}
catch (ExchangeRateLimitException ex)
{
    Console.WriteLine($"Rate limited. Retry after: {ex.RetryAfter}");
}
catch (ExchangeException ex)
{
    Console.WriteLine($"Exchange error [{ex.ExchangeName}]: {ex.Message} (code: {ex.ErrorCode})");
}
```
