using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// Bollinger Bands Breakout strategy: buys when price breaks above the upper band
/// with above-average volume, sells when price touches the lower band or stop-loss/take-profit hit.
/// </summary>
public class BollingerBandsStrategy : IBacktestStrategy
{
    private int _bollingerPeriod = 20;
    private decimal _stdDevMultiplier = 2.0m;
    private decimal _stopLossPercent = 0.02m;
    private decimal _takeProfitPercent = 0.04m;
    private int _volumeAvgPeriod = 20;

    /// <inheritdoc/>
    public string Name => "Bollinger Bands Breakout";
    /// <inheritdoc/>
    public string Description => "Buy on breakout above upper Bollinger Band with volume confirmation; sell on lower band touch or SL/TP.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _bollingerPeriod = parameters.Get("BollingerPeriod", 20);
        _stdDevMultiplier = parameters.Get("StdDevMultiplier", 2.0m);
        _stopLossPercent = parameters.Get("StopLossPercent", 0.02m);
        _takeProfitPercent = parameters.Get("TakeProfitPercent", 0.04m);
        _volumeAvgPeriod = parameters.Get("VolumeAvgPeriod", 20);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < _bollingerPeriod + 1)
            return TradeSignal.Hold;

        var closes = historicalCandles.Select(c => c.Close).ToList();
        var (upper, _, lower) = BollingerBands.Calculate(closes, _bollingerPeriod, _stdDevMultiplier);

        var last = historicalCandles.Count - 1;
        var prev = last - 1;

        if (!upper[last].HasValue || !lower[last].HasValue || !upper[prev].HasValue)
            return TradeSignal.Hold;

        // Manage open position
        if (portfolio.OpenTrade != null && portfolio.OpenTradeInfo != null)
        {
            var info = portfolio.OpenTradeInfo;

            // Initialize SL/TP on first candle after entry
            if (info.StopLoss == 0)
            {
                info.StopLoss = info.EntryPrice * (1 - _stopLossPercent);
                info.TakeProfit = info.EntryPrice * (1 + _takeProfitPercent);
            }

            if (candle.Low <= info.StopLoss)
                return TradeSignal.Sell;

            if (info.TakeProfit.HasValue && candle.High >= info.TakeProfit.Value)
                return TradeSignal.Sell;

            // Sell when price touches lower band (mean reversion exit)
            if (candle.Close <= lower[last]!.Value)
                return TradeSignal.Sell;

            return TradeSignal.Hold;
        }

        // Volume confirmation: current volume > average volume
        var volumePeriod = Math.Min(_volumeAvgPeriod, historicalCandles.Count);
        var avgVolume = historicalCandles.TakeLast(volumePeriod).Average(c => c.Volume);
        var highVolume = candle.Volume > avgVolume;

        // Buy when price breaks above upper band with volume confirmation
        var prevClose = historicalCandles[prev].Close;
        var brokeAbove = prevClose <= upper[prev]!.Value && candle.Close > upper[last]!.Value;

        if (brokeAbove && highVolume)
            return TradeSignal.Buy;

        return TradeSignal.Hold;
    }
}
