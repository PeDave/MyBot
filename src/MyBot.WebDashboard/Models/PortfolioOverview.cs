namespace MyBot.WebDashboard.Models;

public class PortfolioOverview
{
    public decimal TotalBalanceUsd { get; set; }
    public Dictionary<string, CoinBalance> CoinBreakdown { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
