namespace MyBot.WebDashboard.Models;

public class ExchangeBalance
{
    public string Name { get; set; } = string.Empty;
    public decimal TotalUsd { get; set; }
    public List<AssetBalance> Balances { get; set; } = new();
}

public class AssetBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total => Free + Locked;
    public decimal UsdValue { get; set; }
}
