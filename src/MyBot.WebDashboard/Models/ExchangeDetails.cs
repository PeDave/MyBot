using MyBot.Core.Models;

namespace MyBot.WebDashboard.Models;

public class ExchangeDetails
{
    public string Name { get; set; } = string.Empty;
    public AccountBalances Accounts { get; set; } = new();
}
