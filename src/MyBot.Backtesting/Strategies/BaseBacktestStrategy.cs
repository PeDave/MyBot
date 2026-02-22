using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies;

/// <summary>
/// Base class for backtesting strategies. Provides a minimum holding-period guard
/// so strategies are not triggered again within <see cref="MinimumHoldingHours"/> hours
/// of the last trade. Concrete strategies inherit this class and implement
/// <see cref="OnCandle"/> via the abstract <see cref="EvaluateSignal"/> method.
/// </summary>
public abstract class BaseBacktestStrategy : IBacktestStrategy
{
    /// <summary>Minimum hours that must elapse between consecutive trades.</summary>
    protected int MinimumHoldingHours { get; set; } = 4;

    private DateTime? _lastTradeTime;

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract string Description { get; }

    /// <inheritdoc/>
    public abstract void Initialize(StrategyParameters parameters);

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        // Allow sell signals through even during the holding period so stop-losses are never blocked.
        if (portfolio.OpenTrade != null)
            return EvaluateSignal(candle, portfolio, historicalCandles);

        if (!CanTrade(candle.Timestamp))
            return TradeSignal.Hold;

        return EvaluateSignal(candle, portfolio, historicalCandles);
    }

    /// <summary>
    /// Evaluates market conditions and returns a trade signal for the current candle.
    /// The minimum-holding-period guard is applied by the base class before this method
    /// is called for new entries; exit signals are always evaluated without restriction.
    /// </summary>
    protected abstract TradeSignal EvaluateSignal(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles);

    /// <summary>Returns <c>true</c> when enough time has passed since the last trade.</summary>
    protected bool CanTrade(DateTime currentTime)
    {
        if (!_lastTradeTime.HasValue) return true;
        return (currentTime - _lastTradeTime.Value).TotalHours >= MinimumHoldingHours;
    }

    /// <summary>Records the time of the most recent trade to enforce the holding period.</summary>
    protected void RecordTrade(DateTime time)
    {
        _lastTradeTime = time;
    }
}
