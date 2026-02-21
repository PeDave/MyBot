using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// Triple EMA + RSI strategy: trend following with triple EMA alignment and RSI confirmation.
/// Buys when FastEMA > MidEMA > SlowEMA and RSI > 50; uses a trailing stop for exits.
/// </summary>
public class TripleEmaRsiStrategy : IBacktestStrategy
{
    private int _fastEmaPeriod = 8;
    private int _midEmaPeriod = 21;
    private int _slowEmaPeriod = 55;
    private int _rsiPeriod = 14;
    private decimal _rsiOverbought = 70m;
    private decimal _rsiOversold = 30m;
    private decimal _trailingStopPercent = 0.05m;

    /// <inheritdoc/>
    public string Name => "Triple EMA + RSI";
    /// <inheritdoc/>
    public string Description => "Trend following using triple EMA alignment (FastEMA > MidEMA > SlowEMA) with RSI confirmation and trailing stop.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _fastEmaPeriod = parameters.Get("FastEmaPeriod", 8);
        _midEmaPeriod = parameters.Get("MidEmaPeriod", 21);
        _slowEmaPeriod = parameters.Get("SlowEmaPeriod", 55);
        _rsiPeriod = parameters.Get("RsiPeriod", 14);
        _rsiOverbought = parameters.Get("RsiOverbought", 70m);
        _rsiOversold = parameters.Get("RsiOversold", 30m);
        _trailingStopPercent = parameters.Get("TrailingStopPercent", 0.05m);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        var minPeriod = _slowEmaPeriod + _rsiPeriod + 2;
        if (historicalCandles.Count < minPeriod)
            return TradeSignal.Hold;

        var closes = historicalCandles.Select(c => c.Close).ToList();
        var fastEma = EMA.Calculate(closes, _fastEmaPeriod);
        var midEma = EMA.Calculate(closes, _midEmaPeriod);
        var slowEma = EMA.Calculate(closes, _slowEmaPeriod);
        var rsi = RSI.Calculate(closes, _rsiPeriod);

        var last = closes.Count - 1;

        if (!fastEma[last].HasValue || !midEma[last].HasValue ||
            !slowEma[last].HasValue || !rsi[last].HasValue)
            return TradeSignal.Hold;

        var fast = fastEma[last]!.Value;
        var mid = midEma[last]!.Value;
        var slow = slowEma[last]!.Value;
        var rsiVal = rsi[last]!.Value;

        // Manage open position: update trailing stop and check for exit
        if (portfolio.OpenTrade != null && portfolio.OpenTradeInfo != null)
        {
            var info = portfolio.OpenTradeInfo;

            // Update highest price since entry for trailing stop
            if (candle.High > info.HighestPriceSinceEntry)
                info.HighestPriceSinceEntry = candle.High;

            // Update trailing stop (only moves up, never down)
            var newTrailing = info.HighestPriceSinceEntry * (1 - _trailingStopPercent);
            if (!info.TrailingStop.HasValue || newTrailing > info.TrailingStop.Value)
                info.TrailingStop = newTrailing;

            // Exit if trailing stop hit
            if (info.TrailingStop.HasValue && candle.Low <= info.TrailingStop.Value)
                return TradeSignal.Sell;

            // Exit if trend reverses: FastEMA drops below MidEMA or RSI goes below 40
            if (fast < mid || rsiVal < 40m)
                return TradeSignal.Sell;

            return TradeSignal.Hold;
        }

        // Entry: FastEMA > MidEMA > SlowEMA (strong uptrend) AND RSI > 50 (momentum)
        var uptrend = fast > mid && mid > slow;
        if (uptrend && rsiVal > 50m)
            return TradeSignal.Buy;

        return TradeSignal.Hold;
    }
}
