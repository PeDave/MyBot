namespace MyBot.Backtesting.Engine;

/// <summary>Defines how position size is determined.</summary>
public enum PositionSizingMode
{
    /// <summary>Use a fixed percentage of the total portfolio value.</summary>
    PercentageOfPortfolio,
    /// <summary>Use a fixed amount of base currency.</summary>
    FixedAmount
}

/// <summary>Configuration settings for a backtest run.</summary>
public class BacktestConfig
{
    /// <summary>Starting portfolio balance in base currency. Default: 10,000.</summary>
    public decimal InitialBalance { get; set; } = 10000m;
    /// <summary>Taker fee rate (e.g., 0.001 = 0.1%). Default: 0.1%.</summary>
    public decimal TakerFeeRate { get; set; } = 0.001m;
    /// <summary>Maker fee rate (e.g., 0.0008 = 0.08%). Default: 0.08%.</summary>
    public decimal MakerFeeRate { get; set; } = 0.0008m;
    /// <summary>Slippage rate applied to market orders (e.g., 0.0001 = 0.01%). Default: 0.01%.</summary>
    public decimal SlippageRate { get; set; } = 0.0001m;
    /// <summary>Position sizing mode. Default: PercentageOfPortfolio.</summary>
    public PositionSizingMode SizingMode { get; set; } = PositionSizingMode.PercentageOfPortfolio;
    /// <summary>Position size as a fraction of portfolio (e.g., 0.95 = 95%) or fixed amount. Default: 0.95.</summary>
    public decimal PositionSize { get; set; } = 0.95m;
    /// <summary>Maximum fraction of account value allowed in a single position (e.g., 0.05 = 5%). Default: 5%.</summary>
    public decimal MaxPositionSizePercent { get; set; } = 0.05m;
    /// <summary>Maximum loss allowed per trade as a fraction of the trade entry price (e.g., 0.05 = 5%). Default: 5%.</summary>
    public decimal MaxLossPerTradePercent { get; set; } = 0.05m;
    /// <summary>Maximum loss allowed in a single day as a fraction of account value (e.g., 0.10 = 10%). Default: 10%.</summary>
    public decimal MaxDailyLossPercent { get; set; } = 0.10m;
}
