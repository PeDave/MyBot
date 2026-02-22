using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// MACD Trend Following strategy: buys when MACD crosses above the signal line with
/// sufficient crossover strength, sells on the reverse crossover or a 3% stop-loss.
/// A minimum holding period prevents rapid in-and-out trading.
/// </summary>
public class MacdTrendStrategy : BaseBacktestStrategy
{
    private int _fastPeriod = 12;
    private int _slowPeriod = 26;
    private int _signalPeriod = 9;
    private decimal _stopLossPercent = 0.03m;
    private decimal _minCrossoverStrengthPercent = 0.001m;

    /// <inheritdoc/>
    public override string Name => "MACD Trend";
    /// <inheritdoc/>
    public override string Description => $"Buys when MACD({_fastPeriod},{_slowPeriod},{_signalPeriod}) crosses above signal line with strength ≥ {_minCrossoverStrengthPercent:P1} of price; sells on reverse crossover or {_stopLossPercent:P0} stop-loss.";

    /// <inheritdoc/>
    public override void Initialize(StrategyParameters parameters)
    {
        _fastPeriod = parameters.Get("FastPeriod", 12);
        _slowPeriod = parameters.Get("SlowPeriod", 26);
        _signalPeriod = parameters.Get("SignalPeriod", 9);
        _stopLossPercent = parameters.Get("StopLossPercent", 0.03m);
        _minCrossoverStrengthPercent = parameters.Get("MinCrossoverStrengthPercent", 0.001m);
        MinimumHoldingHours = parameters.Get("MinimumHoldingHours", 6);
    }

    /// <inheritdoc/>
    protected override TradeSignal EvaluateSignal(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < Math.Max(_slowPeriod, 50) + _signalPeriod)
            return TradeSignal.Hold;

        var closes = historicalCandles.Select(c => c.Close).ToList();
        var (macdLine, signalLine, _) = MACD.Calculate(closes, _fastPeriod, _slowPeriod, _signalPeriod);

        var last = closes.Count - 1;
        var prev = last - 1;

        if (!macdLine[last].HasValue || !signalLine[last].HasValue ||
            !macdLine[prev].HasValue || !signalLine[prev].HasValue)
            return TradeSignal.Hold;

        // Stop-loss: exit if the open trade has fallen by more than _stopLossPercent
        if (portfolio.OpenTrade != null)
        {
            var lossPct = (candle.Close - portfolio.OpenTrade.EntryPrice) / portfolio.OpenTrade.EntryPrice;
            if (lossPct < -_stopLossPercent)
            {
                RecordTrade(candle.Timestamp);
                return TradeSignal.Sell;
            }
        }

        var macdCrossedAbove = macdLine[prev] <= signalLine[prev] && macdLine[last] > signalLine[last];
        var macdCrossedBelow = macdLine[prev] >= signalLine[prev] && macdLine[last] < signalLine[last];

        if (macdCrossedAbove && portfolio.OpenTrade == null)
        {
            // Only trade on crossovers with meaningful separation (configurable, default 0.1% of price)
            var crossoverStrength = Math.Abs(macdLine[last]!.Value - signalLine[last]!.Value);
            var minStrength = candle.Close * _minCrossoverStrengthPercent;
            if (crossoverStrength < minStrength)
                return TradeSignal.Hold;

            RecordTrade(candle.Timestamp);
            return TradeSignal.Buy;
        }

        if (macdCrossedBelow && portfolio.OpenTrade != null)
        {
            RecordTrade(candle.Timestamp);
            return TradeSignal.Sell;
        }

        return TradeSignal.Hold;
    }
}
