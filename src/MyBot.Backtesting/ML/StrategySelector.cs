using MyBot.Backtesting.Analysis;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Models;
using MyBot.Backtesting.Strategies;

namespace MyBot.Backtesting.ML;

/// <summary>
/// ML-alapú stratégia szelektor, amely több stratégiát backtestel és
/// a jelenlegi piaci környezetnek megfelelőt választja.
/// </summary>
public static class StrategySelector
{
    /// <summary>Egyszerűsített piaci rezsim osztályozás (bull, bear, sideways).</summary>
    public enum MarketRegime
    {
        /// <summary>Emelkedő piac – ár SMA200 felett és felfelé tartó trend.</summary>
        Bull,
        /// <summary>Eső piac – ár SMA200 alatt és lefelé tartó trend.</summary>
        Bear,
        /// <summary>Oldalazó piac – lapos SMA200, alacsony momentum.</summary>
        Sideways
    }

    /// <summary>
    /// Értékeli az aktuális piaci rezsimet a legutóbbi gyertyák alapján.
    /// Az elemzéshez a meglévő <see cref="MarketRegimeDetector"/> osztályt használja.
    /// </summary>
    /// <param name="recentCandles">Legutóbbi gyertyák (időrendben).</param>
    /// <param name="lookbackDays">Visszatekintési ablak napokban (alapértelmezett: 90).</param>
    /// <returns>Az aktuális piaci rezsim.</returns>
    public static MarketRegime ClassifyRegime(List<OHLCVCandle> recentCandles, int lookbackDays = 90)
    {
        if (recentCandles == null || recentCandles.Count == 0)
            return MarketRegime.Sideways;

        var slice = recentCandles.TakeLast(lookbackDays).ToList();
        var regime = MarketRegimeDetector.DetectRegime(slice, slice.Count - 1);

        if (regime == null)
            return MarketRegime.Sideways;

        return regime.MarketPhase switch
        {
            MarketPhase.Bull     => MarketRegime.Bull,
            MarketPhase.Bear     => MarketRegime.Bear,
            _                    => MarketRegime.Sideways
        };
    }

    /// <summary>
    /// Minden stratégiát backtestel ugyanazon az adatokon és visszaadja a teljesítmény metrikákat.
    /// </summary>
    /// <param name="strategies">Kiértékelendő stratégiák listája.</param>
    /// <param name="historicalData">Historikus gyertyaadatok.</param>
    /// <param name="initialCapital">Kezdeti tőke.</param>
    /// <returns>Stratégia neve → BacktestResult leképezés.</returns>
    public static Dictionary<string, BacktestResult> EvaluateStrategies(
        List<IBacktestStrategy> strategies,
        List<OHLCVCandle> historicalData,
        decimal initialCapital)
    {
        if (strategies == null || strategies.Count == 0)
            return new Dictionary<string, BacktestResult>();
        if (historicalData == null || historicalData.Count == 0)
            return new Dictionary<string, BacktestResult>();

        var engine  = new BacktestEngine();
        var config  = new BacktestConfig
        {
            InitialBalance         = initialCapital,
            TakerFeeRate           = 0.0005m,
            MakerFeeRate           = 0.0005m,
            SlippageRate           = 0.0001m,
            SizingMode             = PositionSizingMode.PercentageOfPortfolio,
            PositionSize           = 0.95m,
            MaxLossPerTradePercent = 0m
        };

        var results = new Dictionary<string, BacktestResult>();
        foreach (var strategy in strategies)
        {
            var result = engine.RunBacktest(strategy, historicalData, initialCapital, config);
            results[strategy.Name] = result;
        }

        return results;
    }

    /// <summary>
    /// Kiválasztja a legjobb stratégiát a backtest eredmények és az aktuális piaci rezsim alapján.
    /// Elsődleges szempont: Sharpe ráta. Bull rezsimben a trend-követő stratégiák magasabb súlyt kapnak.
    /// </summary>
    /// <param name="strategyResults">Backtest eredmények (EvaluateStrategies kimenetelből).</param>
    /// <param name="currentRegime">Aktuális piaci rezsim.</param>
    /// <returns>A legjobb stratégia neve szerinti IBacktestStrategy példány keresési kulcsa.</returns>
    public static string SelectBestStrategy(
        Dictionary<string, BacktestResult> strategyResults,
        MarketRegime currentRegime)
    {
        if (strategyResults == null || strategyResults.Count == 0)
            return string.Empty;

        // Alapértelmezett súlyozás Sharpe ráta alapján, rezsim-specifikus bónusszal
        var scored = strategyResults
            .Select(kvp =>
            {
                var sharpe = kvp.Value.Metrics.SharpeRatio;
                var bonus  = GetRegimeBonus(kvp.Key, currentRegime);
                return (Name: kvp.Key, Score: sharpe + bonus);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count == 0)
            return strategyResults.Keys.First();

        return scored[0].Name;
    }

    /// <summary>
    /// Rezsim-specifikus bónuszpontot ad a stratégiáknak.
    /// Trend-követő stratégiák (BTC Macro MA) jobb pontot kapnak Bull/Bear rezsimben,
    /// a Buy &amp; Hold Bull rezsimben kap bónuszt.
    /// </summary>
    private static decimal GetRegimeBonus(string strategyName, MarketRegime regime)
    {
        var nameLower = strategyName.ToLowerInvariant();

        return regime switch
        {
            MarketRegime.Bull =>
                nameLower.Contains("macro") || nameLower.Contains("trend") ? 0.3m :
                nameLower.Contains("buy") && nameLower.Contains("hold")    ? 0.2m :
                0m,
            MarketRegime.Bear =>
                nameLower.Contains("macro") || nameLower.Contains("trend") ? 0.2m :
                0m,
            _ => 0m
        };
    }
}
