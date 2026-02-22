using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// Volatility Breakout (Turtle Trading) strategy: buys when price breaks above
/// the upper Donchian Channel, exits when price breaks below the lower channel
/// or stop-loss is hit. Position sizing uses ATR-based volatility.
/// </summary>
public class VolatilityBreakoutStrategy : IBacktestStrategy
{
    private int _channelPeriod = 20;
    private int _atrPeriod = 14;
    private decimal _atrMultiplier = 2.0m;
    private decimal _positionSizePercent = 0.02m;

    /// <inheritdoc/>
    public string Name => "Volatility Breakout";
    /// <inheritdoc/>
    public string Description => "Classic Turtle Trading: buys Donchian Channel breakouts with ATR-based stop-loss.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _channelPeriod = parameters.Get("ChannelPeriod", 20);
        _atrPeriod = parameters.Get("AtrPeriod", 14);
        _atrMultiplier = parameters.Get("AtrMultiplier", 2.0m);
        _positionSizePercent = parameters.Get("PositionSizePercent", 0.02m);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        var minPeriod = Math.Max(_channelPeriod, _atrPeriod) + 1;
        if (historicalCandles.Count < minPeriod)
            return TradeSignal.Hold;

        var last = historicalCandles.Count - 1;
        var highs = historicalCandles.Select(c => c.High).ToList();
        var lows = historicalCandles.Select(c => c.Low).ToList();
        var closes = historicalCandles.Select(c => c.Close).ToList();

        var (upper, _, lower) = DonchianChannel.Calculate(highs, lows, _channelPeriod);
        var atr = ATR.Calculate(highs, lows, closes, _atrPeriod);

        if (!upper[last].HasValue || !lower[last].HasValue || !atr[last].HasValue)
            return TradeSignal.Hold;

        // Manage open position
        if (portfolio.OpenTrade != null && portfolio.OpenTradeInfo != null)
        {
            var info = portfolio.OpenTradeInfo;

            // Initialize ATR-based stop-loss on first candle after entry
            if (info.StopLoss == 0)
                info.StopLoss = info.EntryPrice - (atr[last]!.Value * _atrMultiplier);

            if (candle.Low <= info.StopLoss)
                return TradeSignal.Sell;

            // Exit when price breaks below the lower Donchian channel
            // (use previous period's lower band for the same reason as entry)
            if (!lower[last - 1].HasValue) return TradeSignal.Hold;
            if (candle.Close <= lower[last - 1]!.Value)
                return TradeSignal.Sell;

            return TradeSignal.Hold;
        }

        // Buy on breakout above the upper Donchian channel.
        // Compare current close against the channel from the PREVIOUS candle
        // (upper[prev] = highest high over the period ending at prev candle).
        // This avoids look-ahead bias since the current candle's high is included in upper[last].
        var prev = last - 1;
        if (!upper[prev].HasValue) return TradeSignal.Hold;

        if (candle.Close > upper[prev]!.Value)
            return TradeSignal.Buy;

        return TradeSignal.Hold;
    }
}
