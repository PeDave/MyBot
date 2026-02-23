using MyBot.Core.Interfaces;
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
}
