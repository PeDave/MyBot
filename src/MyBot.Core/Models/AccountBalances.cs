namespace MyBot.Core.Models;

public class AssetBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total => Free + Locked;
    public decimal UsdValue { get; set; }
}

public class AccountBalances
{
    public List<AssetBalance> Spot { get; set; } = new();
    public List<AssetBalance> Futures { get; set; } = new();
    public List<AssetBalance> UsdtMFutures { get; set; } = new();
    public List<AssetBalance> CoinMFutures { get; set; } = new();
    public List<AssetBalance> Earn { get; set; } = new();
    public List<AssetBalance> Bot { get; set; } = new();
    public List<AssetBalance> Wealth { get; set; } = new(); // BingX specifikus
    public List<AssetBalance> Unified { get; set; } = new(); // Bybit specifikus

    public decimal TotalUsd =>
        Spot.Sum(a => a.UsdValue) +
        Futures.Sum(a => a.UsdValue) +
        UsdtMFutures.Sum(a => a.UsdValue) +
        CoinMFutures.Sum(a => a.UsdValue) +
        Earn.Sum(a => a.UsdValue) +
        Bot.Sum(a => a.UsdValue) +
        Wealth.Sum(a => a.UsdValue) +
        Unified.Sum(a => a.UsdValue);
}
