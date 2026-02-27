# MyBot - Cryptocurrency Exchange Wrapper Library

A unified C# wrapper/bridge library for multiple cryptocurrency exchanges, providing a single consistent API interface across Bitget, BingX, MEXC, and Bybit.

## Project Structure

```
MyBot/
├── MyBot.slnx                    # Solution file
├── src/
│   ├── MyBot.Core/               # Core interfaces, models, and exceptions
│   ├── MyBot.Exchanges/          # Exchange wrapper implementations
│   │   ├── Bitget/BitgetWrapper.cs
│   │   ├── BingX/BingXWrapper.cs
│   │   ├── Mexc/MexcWrapper.cs
│   │   └── Bybit/BybitWrapper.cs
│   ├── MyBot.Console/            # Exchange demo console
│   ├── MyBot.Backtesting/        # Backtesting framework
│   │   ├── Data/
│   │   │   ├── HistoricalDataManager.cs
│   │   │   └── MultiTimeframeAggregator.cs   # Daily/weekly OHLCV aggregation
│   │   ├── Engine/
│   │   │   ├── BacktestEngine.cs
│   │   │   ├── VirtualPortfolio.cs
│   │   │   └── BacktestConfig.cs
│   │   ├── Strategies/
│   │   │   ├── IBacktestStrategy.cs
│   │   │   ├── BuyAndHoldStrategy.cs         # Baseline buy-and-hold
│   │   │   ├── BtcMacroMaStrategy.cs         # Bull Band + TP + Scale-in
│   │   │   ├── BtcMacroMaTrendWithTpScaleInStrategy.cs  # Pine Script port (active)
│   │   │   └── Examples/                     # Example strategies
│   │   ├── ML/
│   │   │   └── StrategySelector.cs           # ML-based strategy selection
│   │   ├── Analysis/
│   │   │   └── MarketRegimeDetector.cs
│   │   ├── Indicators/
│   │   └── Reports/
│   └── MyBot.Backtesting.Console/ # Backtesting CLI app
│       └── Program.cs
└── README.md
```

## Supported Exchanges

| Exchange | SDK Package      | Version |
|----------|------------------|---------|
| Bitget   | JK.Bitget.Net    | 3.*     |
| BingX    | JK.BingX.Net     | 3.*     |
| MEXC     | JK.Mexc.Net      | 4.*     |
| Bybit    | Bybit.Net        | 6.*     |

---

## Backtesting Framework

### Quick Start

```bash
# Run with default settings (BTCUSDT, 2020–present, synthetic data)
dotnet run --project src/MyBot.Backtesting.Console

# Run with specific symbols and date range
dotnet run --project src/MyBot.Backtesting.Console -- \
  --symbols BTCUSDT,ETHUSDT \
  --start 2020-01-01 \
  --end 2024-12-31
```

### Strategies

#### Buy & Hold (`BuyAndHoldStrategy`)
Simple baseline strategy: buys at the first candle and holds forever. Useful as a performance benchmark for active strategies.

#### BTC Macro MA Trend (`BtcMacroMaStrategy`)
Based on the **Bull Market Support Band** concept (PineScript origin). Uses weekly SMA20 and EMA21 to define bull/bear zones, with multi-timeframe aggregation.

| Parameter      | Default | Description |
|----------------|---------|-------------|
| `Use200DFilter`| `false` | Only enter if price > 200D SMA |
| `UseTrailing`  | `true`  | Enable trailing stop |
| `TrailPct`     | `5.0`   | Trailing stop distance (%) |
| `TpStepPct`    | `10.0`  | Take-profit step size (%) |
| `TpClosePct`   | `20.0`  | Position % to close at each TP |
| `MaxSteps`     | `5`     | Maximum TP steps |
| `UseScaleIn`   | `true`  | Allow scale-in after pullback |
| `PullbackPct`  | `6.0`   | Minimum pullback (%) to trigger scale-in |
| `AddSizePct`   | `20.0`  | Scale-in position size (% of initial capital) |

**Entry logic**: Price crosses from below the bull band into or above it (red→green color flip).  
**Exit logic**: Price falls below the bull band (green→red) or trailing stop triggers.  
**Scale-in**: After at least one TP level is reached, if price pulls back ≥ `PullbackPct` from the peak and then re-breaks the band, a new long is opened.

## BTC Macro MA Trend Strategy

**Forrás:** Pine Script portolás

**Leírás:**
- Bull Market Support Band (SMA20W + EMA21W) alapú long stratégia
- Belépés: bear zónából bull zónába váltás (piros → zöld)
- Kilépés: bull zónából bear zónába váltás (zöld → piros) VAGY trailing stop
- Lépcsős profit-taking (TP): 10%-onként 20% pozíció zárás
- Scale-in pullback után: 6%+ esés után re-break esetén újra belépés

**Paraméterek:**
- `Use200DFilter`: 200D SMA filter használata (default: false)
- `TpStepPct`: TP lépés % (default: 10)
- `MaxSteps`: Max TP lépcsők (default: 5)
- `PullbackPct`: Min pullback % (default: 6)
- `UseTrailing`: Trailing stop használata (default: true)
- `TrailPct`: Trailing stop % (default: 5)

**Futtatás:**
```bash
cd src/MyBot.Backtesting.Console
dotnet run
```

---

### Multi-Timeframe Aggregation (`MultiTimeframeAggregator`)

Aggregates lower-timeframe candles into daily and weekly candles for indicator calculation:

```csharp
var dailyCandles  = MultiTimeframeAggregator.ToDailyCandles(hourlyCandles);
var weeklyCandles = MultiTimeframeAggregator.ToWeeklyCandles(hourlyCandles);
```

OHLCV aggregation: Open = first, High = max, Low = min, Close = last, Volume = sum.  
Weekly grouping is ISO week / Monday-based.

### ML-Based Strategy Selection (`StrategySelector`)

Evaluates multiple strategies on the same data and selects the best one based on the current market regime:

```csharp
// Evaluate all strategies
var results = StrategySelector.EvaluateStrategies(strategies, candles, 10_000m);

// Classify current market regime
var regime = StrategySelector.ClassifyRegime(candles.TakeLast(90).ToList());

// Select best strategy (Sharpe ratio + regime bonus)
var bestStrategyName = StrategySelector.SelectBestStrategy(results, regime);
```

**Market regimes**: `Bull`, `Bear`, `Sideways`  
**Selection criterion**: Sharpe ratio with regime-specific bonuses (trend-following strategies score higher in bull/bear markets).

### Performance Metrics

Each backtest result includes:
- Total return & annualized return
- Max drawdown (absolute + %)
- Sharpe ratio & Sortino ratio
- Win rate, profit factor
- Average win/loss, largest win/loss
- Average holding period

---

## Exchange API

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

### Quick Start

```csharp
using Microsoft.Extensions.Logging;
using MyBot.Exchanges.Bybit;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
using var bybit = new BybitWrapper("apiKey", "apiSecret",
    loggerFactory.CreateLogger<BybitWrapper>());

var ticker = await bybit.GetTickerAsync("BTCUSDT");
Console.WriteLine($"BTC Price: {ticker.LastPrice}");
```

## 🌐 Portfolio Dashboard

Web interface for viewing balances across all exchanges.

### Getting Started

```bash
cd src/MyBot.WebDashboard
dotnet run
```

Open your browser at: **http://localhost:5000**

### Features

- ✅ All exchange balances (Bitget, BingX, MEXC, Bybit)
- ✅ Total portfolio value in USD
- ✅ Per-coin breakdown
- ✅ Visual allocation chart (pie chart)
- ✅ Auto-refresh every 30 seconds

---

## Building

```bash
dotnet restore
dotnet build
```

## Requirements

- .NET 8.0 SDK or later
- API keys from the exchange(s) you want to use (optional – falls back to synthetic data)

## Error Handling

All wrappers throw typed exceptions from `MyBot.Core.Exceptions`:

- `ExchangeException` — base class for all exchange errors
- `ExchangeAuthenticationException` — API key/authentication failures
- `ExchangeRateLimitException` — rate limit exceeded (includes `RetryAfter`)


---

## Új MVP modulok (auth + szignál késleltetés + automation webhook)

A `MyBot.WebDashboard` most tartalmaz egy induló, bővíthető API réteget a kért üzleti modellhez:

- `POST /api/auth/register` – regisztráció (Free/Pro/ProPlus/Admin plan)
- `POST /api/auth/login` – bejelentkezés, bearer token
- `GET /api/account/me` – felhasználó profil/plan
- `POST /api/signals` – új szignál létrehozás (Admin/ProPlus jogosultsággal)
- `GET /api/signals` – plan alapú szignál feed
  - Free: 30 perc késleltetés
  - Pro: 5 perc késleltetés
  - ProPlus/Admin: valós idejű
- `POST /api/automation/events` – n8n/AI agent webhook ingest (`x-source`, `x-event-type` headerekkel)
- `GET /api/automation/events` – legutóbbi automation események listája

### Bővíthetőség más tőzsdékre

A wrapper architektúra már most `IExchangeWrapper` interfészre épül, ezért egy új tőzsde hozzáadásához:

1. Új wrapper osztály a `src/MyBot.Exchanges/<ExchangeName>/` mappában.
2. `IExchangeWrapper` és opcionálisan `IExchangeWebSocketClient` implementálása.
3. Regisztráció a `MyBot.WebDashboard/Program.cs` dependency injection részében.

Ez a minta közvetlenül alkalmazható Bitget mellett Bybit, BingX, OKX, Binance stb. irányba.

### VPS deploy (Docker nélkül)

Ubuntu 24.04 LTS VPS esetén javasolt induló lépések:

```bash
# .NET 8 telepítés
sudo apt update
sudo apt install -y dotnet-sdk-8.0

# build + publish
cd /workspace/MyBot
dotnet publish src/MyBot.WebDashboard/MyBot.WebDashboard.csproj -c Release -o ./publish/web

# futtatás
ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet ./publish/web/MyBot.WebDashboard.dll
```

Nginx reverse proxy-val a `https://labotkripto.com` domain a `localhost:5000` szolgáltatásra irányítható.

> Biztonság: a mostani auth réteg in-memory MVP. Production-ben kötelező adatbázis (PostgreSQL), hashelt+saltolt jelszó (ASP.NET Identity), JWT lejárat/refresh token, rate limit, audit log és HTTPS-only cookie/JWT policy.
