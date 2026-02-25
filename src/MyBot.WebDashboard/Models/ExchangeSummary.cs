using MyBot.Core.Models;

namespace MyBot.WebDashboard.Models;

public class ExchangeSummary
{
    public string Name { get; set; } = string.Empty;
    public decimal TotalUsd { get; set; }
    public Dictionary<string, AccountTypeSummary> Accounts { get; set; } = new();
}

public class AccountTypeSummary
{
    public string Type { get; set; } = string.Empty;
    public decimal TotalUsd { get; set; }
    public List<AssetBalance> Balances { get; set; } = new();
}
