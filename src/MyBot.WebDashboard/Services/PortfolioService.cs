using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using MyBot.WebDashboard.Models;

namespace MyBot.WebDashboard.Services;

public class PortfolioService
{
    private readonly List<IExchangeWrapper> _exchanges;
    private readonly PriceService _priceService;
    private readonly ILogger<PortfolioService> _logger;

    public PortfolioService(
        List<IExchangeWrapper> exchanges,
        PriceService priceService,
        ILogger<PortfolioService> logger)
    {
        _exchanges = exchanges;
        _priceService = priceService;
        _logger = logger;
    }

    public async Task<PortfolioSummary> GetPortfolioAsync()
    {
        var summary = new PortfolioSummary();

        foreach (var exchange in _exchanges)
        {
            try
            {
                var balances = await exchange.GetBalancesAsync();
                var assetBalances = new List<AssetBalance>();
                decimal totalUsd = 0;

                foreach (var balance in balances)
                {
                    var total = balance.Available + balance.Locked;
                    if (total <= 0) continue;

                    var usdPrice = await _priceService.GetUsdPriceAsync(balance.Asset);
                    var usdValue = total * usdPrice;

                    assetBalances.Add(new AssetBalance
                    {
                        Asset = balance.Asset,
                        Free = balance.Available,
                        Locked = balance.Locked,
                        UsdValue = usdValue
                    });

                    totalUsd += usdValue;

                    // Coin breakdown aggregation
                    if (!summary.CoinBreakdown.ContainsKey(balance.Asset))
                    {
                        summary.CoinBreakdown[balance.Asset] = new CoinBalance
                        {
                            Asset = balance.Asset,
                            TotalQuantity = 0,
                            UsdValue = 0,
                            UsdPrice = usdPrice
                        };
                    }

                    summary.CoinBreakdown[balance.Asset].TotalQuantity += total;
                    summary.CoinBreakdown[balance.Asset].UsdValue += usdValue;
                }

                summary.Exchanges.Add(new ExchangeBalance
                {
                    Name = exchange.ExchangeName,
                    TotalUsd = totalUsd,
                    Balances = assetBalances
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch balances from {Exchange}", exchange.ExchangeName);
            }
        }

        summary.TotalBalanceUsd = summary.Exchanges.Sum(e => e.TotalUsd);
        return summary;
    }

    // 1️⃣ Portfolio Overview
    public async Task<PortfolioOverview> GetOverviewAsync()
    {
        var overview = new PortfolioOverview();
        var allBalances = new Dictionary<string, CoinBalance>();

        foreach (var exchange in _exchanges)
        {
            try
            {
                var accounts = await exchange.GetAllAccountBalancesAsync();
                var allAssets = accounts.Spot
                    .Concat(accounts.Futures)
                    .Concat(accounts.UsdtMFutures)
                    .Concat(accounts.CoinMFutures)
                    .Concat(accounts.Earn)
                    .Concat(accounts.Bot)
                    .Concat(accounts.Wealth)
                    .Concat(accounts.Unified);

                foreach (var asset in allAssets)
                {
                    if (asset.Total <= 0) continue;

                    if (!allBalances.ContainsKey(asset.Asset))
                    {
                        var usdPrice = await _priceService.GetUsdPriceAsync(asset.Asset);
                        allBalances[asset.Asset] = new CoinBalance
                        {
                            Asset = asset.Asset,
                            TotalQuantity = 0,
                            UsdValue = 0,
                            UsdPrice = usdPrice
                        };
                    }

                    allBalances[asset.Asset].TotalQuantity += asset.Total;
                    allBalances[asset.Asset].UsdValue += asset.Total * allBalances[asset.Asset].UsdPrice;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get balances from {Exchange}", exchange.ExchangeName);
            }
        }

        overview.CoinBreakdown = allBalances;
        overview.TotalBalanceUsd = allBalances.Values.Sum(c => c.UsdValue);
        return overview;
    }

    // 2️⃣ Exchange Summary
    public async Task<List<ExchangeSummary>> GetExchangeSummariesAsync()
    {
        var summaries = new List<ExchangeSummary>();

        foreach (var exchange in _exchanges)
        {
            try
            {
                var accounts = await exchange.GetAllAccountBalancesAsync();
                await EnrichWithPrices(accounts);

                var summary = new ExchangeSummary
                {
                    Name = exchange.ExchangeName,
                    Accounts = new Dictionary<string, AccountTypeSummary>()
                };

                if (accounts.Spot.Any())
                    summary.Accounts["Spot"] = new AccountTypeSummary { Type = "Spot", TotalUsd = accounts.Spot.Sum(a => a.UsdValue), Balances = accounts.Spot };

                if (accounts.UsdtMFutures.Any())
                    summary.Accounts["USDT-M"] = new AccountTypeSummary { Type = "USDT-M Futures", TotalUsd = accounts.UsdtMFutures.Sum(a => a.UsdValue), Balances = accounts.UsdtMFutures };

                if (accounts.CoinMFutures.Any())
                    summary.Accounts["Coin-M"] = new AccountTypeSummary { Type = "Coin-M Futures", TotalUsd = accounts.CoinMFutures.Sum(a => a.UsdValue), Balances = accounts.CoinMFutures };

                if (accounts.Futures.Any())
                    summary.Accounts["Futures"] = new AccountTypeSummary { Type = "Futures", TotalUsd = accounts.Futures.Sum(a => a.UsdValue), Balances = accounts.Futures };

                if (accounts.Earn.Any())
                    summary.Accounts["Earn"] = new AccountTypeSummary { Type = "Earn", TotalUsd = accounts.Earn.Sum(a => a.UsdValue), Balances = accounts.Earn };

                if (accounts.Bot.Any())
                    summary.Accounts["Bot"] = new AccountTypeSummary { Type = "Bot", TotalUsd = accounts.Bot.Sum(a => a.UsdValue), Balances = accounts.Bot };

                if (accounts.Wealth.Any())
                    summary.Accounts["Wealth"] = new AccountTypeSummary { Type = "Wealth", TotalUsd = accounts.Wealth.Sum(a => a.UsdValue), Balances = accounts.Wealth };

                if (accounts.Unified.Any())
                    summary.Accounts["Unified"] = new AccountTypeSummary { Type = "Unified", TotalUsd = accounts.Unified.Sum(a => a.UsdValue), Balances = accounts.Unified };

                summary.TotalUsd = summary.Accounts.Values.Sum(a => a.TotalUsd);
                summaries.Add(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get summary from {Exchange}", exchange.ExchangeName);
            }
        }

        return summaries;
    }

    // 3️⃣ Exchange Details
    public async Task<ExchangeDetails> GetExchangeDetailsAsync(string exchangeName)
    {
        var exchange = _exchanges.FirstOrDefault(e =>
            e.ExchangeName.Equals(exchangeName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Exchange '{exchangeName}' not found");

        var accounts = await exchange.GetAllAccountBalancesAsync();
        await EnrichWithPrices(accounts);

        return new ExchangeDetails
        {
            Name = exchange.ExchangeName,
            Accounts = accounts
        };
    }

    private async Task EnrichWithPrices(AccountBalances accounts)
    {
        var allAssets = accounts.Spot
            .Concat(accounts.Futures)
            .Concat(accounts.UsdtMFutures)
            .Concat(accounts.CoinMFutures)
            .Concat(accounts.Earn)
            .Concat(accounts.Bot)
            .Concat(accounts.Wealth)
            .Concat(accounts.Unified);

        foreach (var asset in allAssets)
        {
            var usdPrice = await _priceService.GetUsdPriceAsync(asset.Asset);
            asset.UsdValue = asset.Total * usdPrice;
        }
    }
}
