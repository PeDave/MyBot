using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Engine;

/// <summary>Tracks open trade risk management levels for stop-loss, take-profit, and trailing stops.</summary>
public class OpenTradeInfo
{
    /// <summary>Entry price of the open trade.</summary>
    public decimal EntryPrice { get; set; }
    /// <summary>Stop-loss price level (0 = not set).</summary>
    public decimal StopLoss { get; set; }
    /// <summary>Take-profit price level (null = not set).</summary>
    public decimal? TakeProfit { get; set; }
    /// <summary>Highest price observed since trade entry (used for trailing stops).</summary>
    public decimal HighestPriceSinceEntry { get; set; }
    /// <summary>Trailing stop price level (null = not set).</summary>
    public decimal? TrailingStop { get; set; }
}

/// <summary>Simulates a virtual portfolio for backtesting, tracking cash, holdings, and trades.</summary>
public class VirtualPortfolio
{
    private int _nextTradeId = 1;

    /// <summary>Available cash balance.</summary>
    public decimal CashBalance { get; set; }
    /// <summary>Symbol -> quantity of holdings.</summary>
    public Dictionary<string, decimal> Holdings { get; set; } = new();
    /// <summary>All completed trades.</summary>
    public List<Trade> Trades { get; set; } = new();
    /// <summary>Current open trade (if any).</summary>
    public Trade? OpenTrade { get; set; }
    /// <summary>Risk management info for the current open trade (stop-loss, take-profit, trailing stop).</summary>
    public OpenTradeInfo? OpenTradeInfo { get; set; }
    /// <summary>The initial balance when the portfolio was created.</summary>
    public decimal InitialBalance { get; }

    /// <summary>Initializes a new virtual portfolio with the given starting balance.</summary>
    public VirtualPortfolio(decimal initialBalance)
    {
        InitialBalance = initialBalance;
        CashBalance = initialBalance;
    }

    /// <summary>Calculates total portfolio value using the current market price.</summary>
    public decimal GetTotalValue(string symbol, decimal currentPrice)
    {
        var positionValue = Holdings.TryGetValue(symbol, out var qty) ? qty * currentPrice : 0m;
        return CashBalance + positionValue;
    }

    /// <summary>Checks whether the portfolio has enough cash to buy the specified quantity.</summary>
    public bool CanBuy(decimal price, decimal quantity, decimal feeRate)
    {
        var cost = price * quantity;
        var fee = cost * feeRate;
        return CashBalance >= cost + fee;
    }

    /// <summary>Checks whether the portfolio holds enough of the symbol to sell the specified quantity.</summary>
    public bool CanSell(string symbol, decimal quantity)
    {
        return Holdings.TryGetValue(symbol, out var held) && held >= quantity;
    }

    /// <summary>Executes a buy, debiting cash and recording the open trade.</summary>
    public void ExecuteBuy(string symbol, decimal price, decimal quantity, decimal fee, DateTime timestamp)
    {
        var cost = price * quantity;
        CashBalance -= cost + fee;
        Holdings[symbol] = Holdings.GetValueOrDefault(symbol, 0m) + quantity;

        OpenTrade = new Trade
        {
            Id = _nextTradeId++,
            Symbol = symbol,
            EntryTime = timestamp,
            EntryPrice = price,
            Quantity = quantity,
            Direction = TradeDirection.Long,
            Fees = fee
        };
        OpenTradeInfo = new OpenTradeInfo
        {
            EntryPrice = price,
            HighestPriceSinceEntry = price
        };
    }

    /// <summary>Executes a sell, crediting cash and closing the open trade.</summary>
    public void ExecuteSell(string symbol, decimal price, decimal quantity, decimal fee, DateTime timestamp)
    {
        var proceeds = price * quantity;
        CashBalance += proceeds - fee;

        if (Holdings.TryGetValue(symbol, out var held))
        {
            Holdings[symbol] = held - quantity;
            if (Holdings[symbol] <= 0) Holdings.Remove(symbol);
        }

        if (OpenTrade != null)
        {
            OpenTrade.ExitTime = timestamp;
            OpenTrade.ExitPrice = price;
            OpenTrade.Fees += fee;
            var entryCost = OpenTrade.EntryPrice * OpenTrade.Quantity;
            OpenTrade.ProfitLoss = proceeds - entryCost - OpenTrade.Fees;
            OpenTrade.ProfitLossPercentage = entryCost > 0 ? (OpenTrade.ProfitLoss / entryCost) * 100m : 0m;
            Trades.Add(OpenTrade);
            OpenTrade = null;
            OpenTradeInfo = null;
        }
    }
}
