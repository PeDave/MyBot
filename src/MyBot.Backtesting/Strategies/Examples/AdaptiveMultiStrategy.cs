using MyBot.Backtesting.Analysis;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.Examples;

/// <summary>
/// Adaptive multi-strategy that monitors the market regime on each candle and delegates
/// to the most appropriate sub-strategy based on the detected regime.
/// </summary>
public class AdaptiveMultiStrategy : IBacktestStrategy
{
    private readonly MacdTrendStrategy _macdTrend = new();
    private readonly TripleEmaRsiStrategy _tripleEma = new();
    private readonly BollingerBandsStrategy _bollingerBands = new();
    private readonly SupportResistanceStrategy _supportResistance = new();

    private IBacktestStrategy? _activeStrategy;
    private string _activeStrategyName = string.Empty;

    /// <inheritdoc/>
    public string Name => "Adaptive Multi-Strategy";

    /// <inheritdoc/>
    public string Description =>
        "Switches between MACD Trend, Triple EMA+RSI, Bollinger Bands, and Support/Resistance based on real-time market regime detection.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        // Pass any common parameters down; sub-strategies use their own defaults
        _macdTrend.Initialize(parameters);
        _tripleEma.Initialize(parameters);
        _bollingerBands.Initialize(parameters);
        _supportResistance.Initialize(parameters);
        _activeStrategy = null;
        _activeStrategyName = string.Empty;
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        // Detect regime at current candle
        var regime = MarketRegimeDetector.DetectRegime(historicalCandles, historicalCandles.Count - 1);

        // Select sub-strategy based on regime (only switch when no open trade)
        if (regime != null && portfolio.OpenTrade == null)
        {
            var recommended = regime.RecommendedStrategy;
            if (recommended != _activeStrategyName)
            {
                _activeStrategy = SelectStrategy(recommended);
                _activeStrategyName = recommended;
            }
        }

        // Fall back to Support/Resistance if no regime could be detected
        if (_activeStrategy == null)
        {
            _activeStrategy = _supportResistance;
            _activeStrategyName = _supportResistance.Name;
        }

        return _activeStrategy.OnCandle(candle, portfolio, historicalCandles);
    }

    private IBacktestStrategy SelectStrategy(string name) => name switch
    {
        "MACD Trend" => _macdTrend,
        "Triple EMA + RSI" => _tripleEma,
        "Bollinger Bands Breakout" => _bollingerBands,
        _ => _supportResistance
    };
}
