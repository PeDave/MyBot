using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies;

/// <summary>
/// Simple buy-and-hold strategy: buys once at the first candle and holds forever.
/// Useful as a baseline to compare active strategies against.
/// </summary>
public class BuyAndHoldStrategy : IBacktestStrategy
{
    private bool _hasBought = false;

    /// <inheritdoc/>
    public string Name => "Buy & Hold";

    /// <inheritdoc/>
    public string Description => "Simple buy-and-hold strategy – buys at the first candle and holds forever.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _hasBought = false;
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        // Buy once at the beginning
        if (!_hasBought && portfolio.OpenTrade == null)
        {
            _hasBought = true;
            return TradeSignal.Buy;
        }

        // Never sell – hold forever
        return TradeSignal.Hold;
    }
}
