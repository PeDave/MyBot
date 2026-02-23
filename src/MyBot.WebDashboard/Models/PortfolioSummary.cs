namespace MyBot.WebDashboard.Models;

public class PortfolioSummary
{
    public decimal TotalBalanceUsd { get; set; }
    public List<ExchangeBalance> Exchanges { get; set; } = new();
    public Dictionary<string, CoinBalance> CoinBreakdown { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
