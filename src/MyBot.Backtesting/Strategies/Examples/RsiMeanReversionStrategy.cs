using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// RSI Mean Reversion strategy: buys when RSI drops below oversold threshold,
/// sells when RSI rises above overbought threshold or when the position is
/// profitable and RSI has returned to neutral (≥ 50).
/// </summary>
public class RsiMeanReversionStrategy : BaseBacktestStrategy
{
    private int _rsiPeriod = 14;
    private decimal _oversold = 35m;
    private decimal _overbought = 65m;

    /// <inheritdoc/>
    public override string Name => "RSI Mean Reversion";
    /// <inheritdoc/>
    public override string Description => $"Buys when RSI({_rsiPeriod}) < {_oversold} (oversold); sells when RSI > {_overbought} (overbought) or RSI ≥ 50 with ≥ 1% profit.";

    /// <inheritdoc/>
    public override void Initialize(StrategyParameters parameters)
    {
        _rsiPeriod = parameters.Get("RsiPeriod", 14);
        _oversold = parameters.Get("Oversold", 35m);
        _overbought = parameters.Get("Overbought", 65m);
        MinimumHoldingHours = parameters.Get("MinimumHoldingHours", 4);
    }

    /// <inheritdoc/>
    protected override TradeSignal EvaluateSignal(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < _rsiPeriod + 10)
            return TradeSignal.Hold;

        var closes = historicalCandles.Select(c => c.Close).ToList();
        var rsi = RSI.Calculate(closes, _rsiPeriod);

        var currentRsi = rsi[^1];
        if (!currentRsi.HasValue) return TradeSignal.Hold;

        if (currentRsi.Value < _oversold && portfolio.OpenTrade == null)
        {
            RecordTrade(candle.Timestamp);
            return TradeSignal.Buy;
        }

        if (portfolio.OpenTrade != null)
        {
            // Primary exit: RSI has reverted to overbought
            if (currentRsi.Value > _overbought)
            {
                RecordTrade(candle.Timestamp);
                return TradeSignal.Sell;
            }

            // Secondary exit: RSI returned to neutral (≥ 50) and position is at least 1% profitable
            if (currentRsi.Value >= 50m)
            {
                var profitPct = (candle.Close - portfolio.OpenTrade.EntryPrice) / portfolio.OpenTrade.EntryPrice;
                if (profitPct >= 0.01m)
                {
                    RecordTrade(candle.Timestamp);
                    return TradeSignal.Sell;
                }
            }
        }

        return TradeSignal.Hold;
    }
}
