using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// Triple EMA + RSI strategy: trend following with triple EMA alignment and RSI confirmation.
/// Requires a minimum EMA separation (trend strength) before entering, and exits when the
/// trend reverses, RSI signals exhaustion, or EMAs start to converge on a profitable trade.
/// </summary>
public class TripleEmaRsiStrategy : BaseBacktestStrategy
{
    private int _fastEmaPeriod = 8;
    private int _midEmaPeriod = 21;
    private int _slowEmaPeriod = 55;
    private int _rsiPeriod = 14;
    private decimal _rsiOverbought = 75m;
    private decimal _rsiOversold = 40m;
    private decimal _trailingStopPercent = 0.05m;
    private decimal _minEmaSeparation = 0.005m;

    /// <inheritdoc/>
    public override string Name => "Triple EMA + RSI";
    /// <inheritdoc/>
    public override string Description => "Trend following using triple EMA alignment (FastEMA > MidEMA > SlowEMA) with RSI confirmation, EMA separation filter, and trailing stop.";

    /// <inheritdoc/>
    public override void Initialize(StrategyParameters parameters)
    {
        _fastEmaPeriod = parameters.Get("FastEmaPeriod", 8);
        _midEmaPeriod = parameters.Get("MidEmaPeriod", 21);
        _slowEmaPeriod = parameters.Get("SlowEmaPeriod", 55);
        _rsiPeriod = parameters.Get("RsiPeriod", 14);
        _rsiOverbought = parameters.Get("RsiOverbought", 75m);
        _rsiOversold = parameters.Get("RsiOversold", 40m);
        _trailingStopPercent = parameters.Get("TrailingStopPercent", 0.05m);
        _minEmaSeparation = parameters.Get("MinEmaSeparation", 0.005m);
        MinimumHoldingHours = parameters.Get("MinimumHoldingHours", 4);
    }

    /// <inheritdoc/>
    protected override TradeSignal EvaluateSignal(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        var minPeriod = _slowEmaPeriod + _rsiPeriod + 10;
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

        // EMA separation as a fraction of the slow EMA (proxy for trend strength)
        var emaSeparation = slow > 0 ? Math.Abs(fast - slow) / slow : 0m;

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
            {
                RecordTrade(candle.Timestamp);
                return TradeSignal.Sell;
            }

            // Exit if trend reverses: FastEMA drops below MidEMA or RSI goes into oversold
            if (fast < mid || rsiVal < _rsiOversold)
            {
                RecordTrade(candle.Timestamp);
                return TradeSignal.Sell;
            }

            // Exit if RSI overbought (momentum exhaustion)
            if (rsiVal > _rsiOverbought)
            {
                RecordTrade(candle.Timestamp);
                return TradeSignal.Sell;
            }

            // Trailing exit: EMAs converging on a profitable trade indicates trend weakening
            if (emaSeparation < 0.002m)
            {
                var profitPct = (candle.Close - portfolio.OpenTrade.EntryPrice) / portfolio.OpenTrade.EntryPrice;
                if (profitPct > 0.01m)
                {
                    RecordTrade(candle.Timestamp);
                    return TradeSignal.Sell;
                }
            }

            return TradeSignal.Hold;
        }

        // Entry: FastEMA > MidEMA > SlowEMA (strong uptrend), RSI in momentum zone, and trend is strong enough
        var bullishAlignment = fast > mid && mid > slow;
        if (bullishAlignment && emaSeparation >= _minEmaSeparation && rsiVal > 50m && rsiVal < 70m)
        {
            RecordTrade(candle.Timestamp);
            return TradeSignal.Buy;
        }

        return TradeSignal.Hold;
    }
}
