using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Engine;

/// <summary>Order types for simulated order execution.</summary>
public enum SimulatedOrderType { Market, Limit }

/// <summary>Simulates order execution against historical candle data.</summary>
public class OrderSimulator
{
    private readonly BacktestConfig _config;

    /// <summary>Initializes a new <see cref="OrderSimulator"/> with the given configuration.</summary>
    public OrderSimulator(BacktestConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Simulates a market buy order, executing at close price with slippage.
    /// </summary>
    public decimal SimulateMarketBuy(OHLCVCandle candle)
    {
        // Market orders execute at close + slippage
        return candle.Close * (1 + _config.SlippageRate);
    }

    /// <summary>
    /// Simulates a market sell order, executing at close price with slippage.
    /// </summary>
    public decimal SimulateMarketSell(OHLCVCandle candle)
    {
        // Market sell executes at close - slippage
        return candle.Close * (1 - _config.SlippageRate);
    }

    /// <summary>
    /// Checks if a limit buy order would execute within the given candle's price range.
    /// Returns the execution price if it triggers, otherwise null.
    /// </summary>
    public decimal? SimulateLimitBuy(OHLCVCandle candle, decimal limitPrice)
    {
        if (candle.Low <= limitPrice)
            return Math.Min(limitPrice, candle.High);
        return null;
    }

    /// <summary>
    /// Checks if a limit sell order would execute within the given candle's price range.
    /// Returns the execution price if it triggers, otherwise null.
    /// </summary>
    public decimal? SimulateLimitSell(OHLCVCandle candle, decimal limitPrice)
    {
        if (candle.High >= limitPrice)
            return Math.Max(limitPrice, candle.Low);
        return null;
    }

    /// <summary>Calculates the taker fee for a given trade value.</summary>
    public decimal CalculateTakerFee(decimal tradeValue) => tradeValue * _config.TakerFeeRate;

    /// <summary>Calculates the maker fee for a given trade value.</summary>
    public decimal CalculateMakerFee(decimal tradeValue) => tradeValue * _config.MakerFeeRate;

    /// <summary>
    /// Calculates the quantity to buy based on the configured position sizing mode.
    /// </summary>
    public decimal CalculateBuyQuantity(decimal availableCash, decimal price, decimal totalPortfolioValue)
    {
        decimal tradeValue = _config.SizingMode switch
        {
            PositionSizingMode.PercentageOfPortfolio => totalPortfolioValue * _config.PositionSize,
            PositionSizingMode.FixedAmount => _config.PositionSize,
            _ => totalPortfolioValue * _config.PositionSize
        };

        // Cap single position to MaxPositionSizePercent of total portfolio value
        if (_config.MaxPositionSizePercent > 0)
        {
            var maxSinglePosition = totalPortfolioValue * _config.MaxPositionSizePercent;
            tradeValue = Math.Min(tradeValue, maxSinglePosition);
        }

        // Clamp to available cash (minus estimated fee)
        var maxAffordable = availableCash / (price * (1 + _config.TakerFeeRate + _config.SlippageRate));
        var desired = tradeValue / price;
        return Math.Min(desired, maxAffordable);
    }
}
