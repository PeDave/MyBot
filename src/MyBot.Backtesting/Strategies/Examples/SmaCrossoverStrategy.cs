using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// SMA Crossover strategy: buys when fast SMA crosses above slow SMA,
/// sells when fast SMA crosses below slow SMA.
/// </summary>
public class SmaCrossoverStrategy : IBacktestStrategy
{
    private int _fastPeriod = 50;
    private int _slowPeriod = 200;

    /// <inheritdoc/>
    public string Name => "SMA Crossover";
    /// <inheritdoc/>
    public string Description => $"Buys when {_fastPeriod}-period SMA crosses above {_slowPeriod}-period SMA; sells on reverse crossover.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _fastPeriod = parameters.Get("FastPeriod", 50);
        _slowPeriod = parameters.Get("SlowPeriod", 200);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < _slowPeriod + 1)
            return TradeSignal.Hold;

        var closes = historicalCandles.Select(c => c.Close).ToList();
        var fastSma = SMA.Calculate(closes, _fastPeriod);
        var slowSma = SMA.Calculate(closes, _slowPeriod);

        var last = closes.Count - 1;
        var prev = last - 1;

        if (!fastSma[last].HasValue || !slowSma[last].HasValue ||
            !fastSma[prev].HasValue || !slowSma[prev].HasValue)
            return TradeSignal.Hold;

        var fastCrossedAbove = fastSma[prev] <= slowSma[prev] && fastSma[last] > slowSma[last];
        var fastCrossedBelow = fastSma[prev] >= slowSma[prev] && fastSma[last] < slowSma[last];

        if (fastCrossedAbove && portfolio.OpenTrade == null)
            return TradeSignal.Buy;
        if (fastCrossedBelow && portfolio.OpenTrade != null)
            return TradeSignal.Sell;

        return TradeSignal.Hold;
    }
}
