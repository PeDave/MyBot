using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// RSI Mean Reversion strategy: buys when RSI drops below oversold threshold,
/// sells when RSI rises above overbought threshold.
/// </summary>
public class RsiMeanReversionStrategy : IBacktestStrategy
{
    private int _rsiPeriod = 14;
    private decimal _oversold = 30m;
    private decimal _overbought = 70m;

    /// <inheritdoc/>
    public string Name => "RSI Mean Reversion";
    /// <inheritdoc/>
    public string Description => $"Buys when RSI({_rsiPeriod}) < {_oversold} (oversold); sells when RSI > {_overbought} (overbought).";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _rsiPeriod = parameters.Get("RsiPeriod", 14);
        _oversold = parameters.Get("Oversold", 30m);
        _overbought = parameters.Get("Overbought", 70m);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < _rsiPeriod + 2)
            return TradeSignal.Hold;

        var closes = historicalCandles.Select(c => c.Close).ToList();
        var rsi = RSI.Calculate(closes, _rsiPeriod);

        var currentRsi = rsi[^1];
        if (!currentRsi.HasValue) return TradeSignal.Hold;

        if (currentRsi.Value < _oversold && portfolio.OpenTrade == null)
            return TradeSignal.Buy;
        if (currentRsi.Value > _overbought && portfolio.OpenTrade != null)
            return TradeSignal.Sell;

        return TradeSignal.Hold;
    }
}
