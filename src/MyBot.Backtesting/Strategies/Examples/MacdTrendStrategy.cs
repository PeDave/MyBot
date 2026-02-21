using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// MACD Trend Following strategy: buys when MACD crosses above the signal line,
/// sells when MACD crosses below the signal line.
/// </summary>
public class MacdTrendStrategy : IBacktestStrategy
{
    private int _fastPeriod = 12;
    private int _slowPeriod = 26;
    private int _signalPeriod = 9;

    /// <inheritdoc/>
    public string Name => "MACD Trend";
    /// <inheritdoc/>
    public string Description => $"Buys when MACD({_fastPeriod},{_slowPeriod},{_signalPeriod}) crosses above signal line; sells on reverse crossover.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _fastPeriod = parameters.Get("FastPeriod", 12);
        _slowPeriod = parameters.Get("SlowPeriod", 26);
        _signalPeriod = parameters.Get("SignalPeriod", 9);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < _slowPeriod + _signalPeriod + 1)
            return TradeSignal.Hold;

        var closes = historicalCandles.Select(c => c.Close).ToList();
        var (macdLine, signalLine, _) = MACD.Calculate(closes, _fastPeriod, _slowPeriod, _signalPeriod);

        var last = closes.Count - 1;
        var prev = last - 1;

        if (!macdLine[last].HasValue || !signalLine[last].HasValue ||
            !macdLine[prev].HasValue || !signalLine[prev].HasValue)
            return TradeSignal.Hold;

        var macdCrossedAbove = macdLine[prev] <= signalLine[prev] && macdLine[last] > signalLine[last];
        var macdCrossedBelow = macdLine[prev] >= signalLine[prev] && macdLine[last] < signalLine[last];

        if (macdCrossedAbove && portfolio.OpenTrade == null)
            return TradeSignal.Buy;
        if (macdCrossedBelow && portfolio.OpenTrade != null)
            return TradeSignal.Sell;

        return TradeSignal.Hold;
    }
}
