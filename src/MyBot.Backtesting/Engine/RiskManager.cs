namespace MyBot.Backtesting.Engine;

/// <summary>
/// Helper class for common risk management calculations used by strategies.
/// </summary>
public class RiskManager
{
    /// <summary>
    /// Calculates a stop-loss price based on the entry price and ATR.
    /// </summary>
    public decimal CalculateStopLoss(decimal entryPrice, decimal atr, decimal multiplier = 2.0m)
    {
        return entryPrice - (atr * multiplier);
    }

    /// <summary>
    /// Calculates a take-profit price based on entry price, stop-loss, and a risk/reward ratio.
    /// </summary>
    public decimal CalculateTakeProfit(decimal entryPrice, decimal stopLoss, decimal riskRewardRatio = 2.0m)
    {
        var risk = entryPrice - stopLoss;
        return entryPrice + (risk * riskRewardRatio);
    }

    /// <summary>
    /// Calculates a trailing stop price: trails below the highest price by <paramref name="trailingPercent"/>.
    /// </summary>
    public decimal CalculateTrailingStop(decimal currentPrice, decimal highestPrice, decimal trailingPercent = 0.05m)
    {
        return highestPrice * (1 - trailingPercent);
    }

    /// <summary>
    /// Calculates position size (number of units) based on account balance, risk percentage, and price levels.
    /// Returns 0 if stop-loss is at or above entry price.
    /// </summary>
    public decimal CalculatePositionSize(decimal accountBalance, decimal riskPercent, decimal entryPrice, decimal stopLoss)
    {
        var riskAmount = accountBalance * riskPercent;
        var priceRisk = entryPrice - stopLoss;
        if (priceRisk <= 0) return 0;
        return riskAmount / priceRisk;
    }
}
