using MyBot.Core.Models;

namespace MyBot.WebDashboard.Models;

public class ExchangeBalance
{
    public string Name { get; set; } = string.Empty;
    public decimal TotalUsd { get; set; }
    public List<AssetBalance> Balances { get; set; } = new();
}
