using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// Support/Resistance Breakout strategy: identifies dynamic support and resistance
/// levels from recent price history and trades confirmed breakouts.
/// Uses ATR-based stop-loss with a configurable risk/reward ratio for take-profit.
/// </summary>
public class SupportResistanceStrategy : IBacktestStrategy
{
    private int _lookbackPeriod = 50;
    private decimal _breakoutThreshold = 0.005m;
    private decimal _volumeMultiplier = 1.0m;
    private decimal _riskRewardRatio = 2.0m;
    private int _atrPeriod = 14;

    /// <inheritdoc/>
    public string Name => "Support/Resistance Breakout";
    /// <inheritdoc/>
    public string Description => "Trades breakouts of dynamic support/resistance levels with ATR-based stop-loss.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _lookbackPeriod = parameters.Get("LookbackPeriod", 50);
        _breakoutThreshold = parameters.Get("BreakoutThreshold", 0.005m);
        _volumeMultiplier = parameters.Get("VolumeMultiplier", 1.0m);
        _riskRewardRatio = parameters.Get("RiskRewardRatio", 2.0m);
        _atrPeriod = parameters.Get("AtrPeriod", 14);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < _lookbackPeriod + _atrPeriod + 1)
            return TradeSignal.Hold;

        var last = historicalCandles.Count - 1;
        var highs = historicalCandles.Select(c => c.High).ToList();
        var lows = historicalCandles.Select(c => c.Low).ToList();
        var closes = historicalCandles.Select(c => c.Close).ToList();

        var atr = ATR.Calculate(highs, lows, closes, _atrPeriod);
        if (!atr[last].HasValue) return TradeSignal.Hold;
        var currentAtr = atr[last]!.Value;

        // Manage open position
        if (portfolio.OpenTrade != null && portfolio.OpenTradeInfo != null)
        {
            var info = portfolio.OpenTradeInfo;

            // Initialize SL/TP on first candle after entry using ATR
            if (info.StopLoss == 0)
            {
                info.StopLoss = info.EntryPrice - (currentAtr * 1.5m);
                var risk = info.EntryPrice - info.StopLoss;
                info.TakeProfit = info.EntryPrice + (risk * _riskRewardRatio);
            }

            if (candle.Low <= info.StopLoss)
                return TradeSignal.Sell;

            if (info.TakeProfit.HasValue && candle.High >= info.TakeProfit.Value)
                return TradeSignal.Sell;

            // Exit if price breaks back below support
            var lookback = historicalCandles
                .Skip(Math.Max(0, last - _lookbackPeriod))
                .Take(_lookbackPeriod)
                .ToList();
            var support = lookback.Min(c => c.Low);
            if (candle.Close < support * (1 - _breakoutThreshold))
                return TradeSignal.Sell;

            return TradeSignal.Hold;
        }

        // Identify resistance: highest high in lookback window (excluding the current candle)
        var windowStart = Math.Max(0, last - _lookbackPeriod);
        var windowEnd = last - 1;
        if (windowEnd < windowStart) return TradeSignal.Hold;

        var resistanceLevel = 0m;
        for (var i = windowStart; i <= windowEnd; i++)
        {
            if (historicalCandles[i].High > resistanceLevel)
                resistanceLevel = historicalCandles[i].High;
        }

        if (resistanceLevel <= 0) return TradeSignal.Hold;

        // Volume confirmation: skipped when VolumeMultiplier <= 0
        var volPeriod = Math.Min(_lookbackPeriod, historicalCandles.Count);
        var avgVolume = historicalCandles.TakeLast(volPeriod).Average(c => c.Volume);
        var highVolume = _volumeMultiplier <= 0 || candle.Volume >= avgVolume * _volumeMultiplier;

        // Buy when price breaks convincingly above resistance with volume
        var breakout = candle.Close > resistanceLevel * (1 + _breakoutThreshold);

        if (breakout && highVolume)
            return TradeSignal.Buy;

        return TradeSignal.Hold;
    }
}
