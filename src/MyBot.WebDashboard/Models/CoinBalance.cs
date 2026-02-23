namespace MyBot.WebDashboard.Models;

public class CoinBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public decimal UsdValue { get; set; }
    public decimal UsdPrice { get; set; }
}
