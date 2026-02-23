using MyBot.Backtesting.Data;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies;

/// <summary>
/// BTC Macro MA Trend Strategy – Pine Script port
///
/// Pine Script forrás:
///   //@version=5
///   strategy("BTC Macro MA Trend Strategy (Bull Band + TP + Scale-in + Trailing)", ...)
///
/// Belépés:
///   - Piros → zöld színváltás (bear → bull zona)
///   - Opcionális: csak 200D SMA felett
///
/// Kilépés:
///   - Zöld → piros színváltás
///   - Trailing stop (5% a csúcstól)
///
/// Take Profit:
///   - Lépcsős: 10%, 20%, 30%, 40%, 50%
///   - Minden TP-nél 20% pozíció zárás
///
/// Scale-in:
///   - Ha volt legalább 1 TP
///   - 6%+ pullback a csúcstól
///   - Re-break felfelé → új long (20% méret)
/// </summary>
public class BtcMacroMaTrendWithTpScaleInStrategy : IBacktestStrategy
{
    // ── Konfiguráció ──────────────────────────────────────────────────────────

    /// <summary>200D SMA szűrő: csak akkor lép be, ha az ár a 200 napos SMA felett van.</summary>
    public bool Use200DFilter { get; set; } = false;

    /// <summary>Lépcsős TP lépés százalék (pl. 10 = minden 10%-os emelkedésnél TP).</summary>
    public double TpStepPct { get; set; } = 10.0;

    /// <summary>Zárandó pozíció aránya minden TP szintnél (%) – tájékoztató jellegű.</summary>
    public double TpClosePct { get; set; } = 20.0;

    /// <summary>Maximális TP lépcsők száma.</summary>
    public int MaxSteps { get; set; } = 5;

    /// <summary>Scale-in engedélyezése pullback után (ha volt TP szint elérve).</summary>
    public bool UseScaleIn { get; set; } = true;

    /// <summary>Minimális pullback százalék a csúcstól a scale-in triggereléséhez.</summary>
    public double PullbackPct { get; set; } = 6.0;

    /// <summary>Scale-in méret a kezdeti tőkéhez képest (%) – tájékoztató jellegű.</summary>
    public double AddSizePct { get; set; } = 20.0;

    /// <summary>Trailing stop használata.</summary>
    public bool UseTrailing { get; set; } = true;

    /// <summary>Trailing stop százalék: a legutóbbi csúcstól való maximális esés.</summary>
    public double TrailPct { get; set; } = 5.0;

    // ── Állapotváltozók ───────────────────────────────────────────────────────

    /// <summary>Belépési ár a pozíció megnyitásakor (TP számításhoz).</summary>
    private decimal? _entryPriceCore;

    /// <summary>Következő TP szint indexe (1-től indul).</summary>
    private int _nextTpIndex = 1;

    /// <summary>Legutóbbi csúcsár (nyitott pozíciónál trailing és scale-in detektáláshoz).</summary>
    private decimal? _lastPeak;

    /// <summary>Scale-in engedélyezve (legalább 1 TP szint elérve).</summary>
    private bool _canAdd = false;

    /// <summary>Elegendő pullback érkezett a scale-in triggeréhez.</summary>
    private bool _pullbackSeen = false;

    /// <summary>Trailing stop alapja: az eddigi legmagasabb High nyitott pozíció esetén.</summary>
    private decimal? _trailBase;

    // ── Interfész implementáció ───────────────────────────────────────────────

    /// <inheritdoc/>
    public string Name => "BTC Macro MA Trend + TP Scale-in";

    /// <inheritdoc/>
    public string Description =>
        "Bull Market Support Band (SMA20W + EMA21W) alapú BTC trend stratégia Pine Script port. " +
        "Belépés: piros→zöld (bear zónából bull zónába), " +
        "kilépés: zöld→piros vagy trailing stop. " +
        "Lépcsős TP (10%-onként 20% pozíció) + scale-in pullback után.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        Use200DFilter = parameters.Get("Use200DFilter", false);
        TpStepPct     = parameters.Get("TpStepPct", 10.0);
        TpClosePct    = parameters.Get("TpClosePct", 20.0);
        MaxSteps      = parameters.Get("MaxSteps", 5);
        UseScaleIn    = parameters.Get("UseScaleIn", true);
        PullbackPct   = parameters.Get("PullbackPct", 6.0);
        AddSizePct    = parameters.Get("AddSizePct", 20.0);
        UseTrailing   = parameters.Get("UseTrailing", true);
        TrailPct      = parameters.Get("TrailPct", 5.0);

        _entryPriceCore = null;
        _nextTpIndex    = 1;
        _lastPeak       = null;
        _canAdd         = false;
        _pullbackSeen   = false;
        _trailBase      = null;
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        // Minimális adatigény: 200 napi gyertya (200D SMA) + 21 heti gyertya (EMA21W)
        if (historicalCandles.Count < 50)
            return TradeSignal.Hold;

        // ── Multi-timeframe aggregáció ────────────────────────────────────────
        var dailyCandles  = TimeframeAggregator.AggregateToDaily(historicalCandles);
        var weeklyCandles = TimeframeAggregator.AggregateToWeekly(dailyCandles);

        // Minimális elégséges adat: 200 napi + 21 heti gyertya
        if (dailyCandles.Count < 200 || weeklyCandles.Count < 21)
            return TradeSignal.Hold;

        // ── Napi 200D SMA ─────────────────────────────────────────────────────
        var dailyCloses  = dailyCandles.Select(c => c.Close).ToList();
        var ma200DValues = SMA.Calculate(dailyCloses, 200);
        var ma200D       = ma200DValues[^1];

        if (!ma200D.HasValue)
            return TradeSignal.Hold;

        // ── Heti indikátorok (SMA20W, EMA21W) ────────────────────────────────
        var weeklyCloses = weeklyCandles.Select(c => c.Close).ToList();
        var sma20WValues = SMA.Calculate(weeklyCloses, 20);
        var ema21WValues = EMA.Calculate(weeklyCloses, 21);

        var sma20W     = sma20WValues[^1];
        var ema21W     = ema21WValues[^1];
        var sma20WPrev = weeklyCloses.Count >= 2 ? sma20WValues[^2] : null;
        var ema21WPrev = weeklyCloses.Count >= 2 ? ema21WValues[^2] : null;

        if (!sma20W.HasValue || !ema21W.HasValue ||
            !sma20WPrev.HasValue || !ema21WPrev.HasValue)
            return TradeSignal.Hold;

        // ── Bull Market Support Band ──────────────────────────────────────────
        // bandTop = max(SMA20W, EMA21W)
        // bandBot = min(SMA20W, EMA21W)
        var bandBot     = Math.Min(sma20W.Value, ema21W.Value);
        var bandBotPrev = Math.Min(sma20WPrev.Value, ema21WPrev.Value);

        if (dailyCandles.Count < 2)
            return TradeSignal.Hold;
        var prevDailyClose = dailyCandles[^2].Close;

        // ── Zóna meghatározása ────────────────────────────────────────────────
        // Előző bar: bear zónában volt-e (bandBot alatt)?
        bool wasBelowPrev = prevDailyClose < bandBotPrev;

        // Jelenlegi bar zónái
        bool isBelowNow      = candle.Close < bandBot;
        bool isInBandOrAbove = candle.Close >= bandBot;

        // ── Színváltás detektálás ─────────────────────────────────────────────
        // Piros → zöld: előző bar bear zónában, jelenlegi sávban vagy fölötte
        bool colorFlipUp   = wasBelowPrev && isInBandOrAbove;
        // Zöld → piros: előző bar NEM bear zónában, jelenlegi bear zónában
        bool colorFlipDown = !wasBelowPrev && isBelowNow;

        // ── Peak tracking (nyitott pozíciónál) ───────────────────────────────
        if (portfolio.OpenTrade != null)
        {
            _lastPeak = _lastPeak == null
                ? candle.High
                : Math.Max(_lastPeak.Value, candle.High);
        }

        // ── Lépcsős TP ellenőrzése ─────────────────────────────────────────────
        if (portfolio.OpenTrade != null && _entryPriceCore.HasValue && _nextTpIndex <= MaxSteps)
        {
            var tpPrice = _entryPriceCore.Value * (1m + (decimal)(TpStepPct * _nextTpIndex) / 100m);
            if (candle.Close >= tpPrice)
            {
                _nextTpIndex++;
                _canAdd = true; // Pullback utáni scale-in engedélyezve
            }
        }

        // ── Trailing stop kezelése ────────────────────────────────────────────
        if (UseTrailing && portfolio.OpenTrade != null)
        {
            _trailBase = _trailBase == null
                ? candle.High
                : Math.Max(_trailBase.Value, candle.High);

            var trailLine = _trailBase.Value * (1.0m - (decimal)TrailPct / 100m);
            if (candle.Close < trailLine)
            {
                _trailBase      = null;
                _entryPriceCore = null;
                _nextTpIndex    = 1;
                return TradeSignal.Sell;
            }
        }

        // ── Kilépés: trendváltás (zöld → piros) ──────────────────────────────
        if (portfolio.OpenTrade != null && colorFlipDown)
        {
            _trailBase      = null;
            _entryPriceCore = null;
            _nextTpIndex    = 1;
            return TradeSignal.Sell;
        }

        // ── Scale-in: pullback utáni újbóli belépés ───────────────────────────
        if (portfolio.OpenTrade == null && _canAdd && UseScaleIn && _lastPeak.HasValue)
        {
            // Elegendő pullback ellenőrzése
            if (candle.Low <= _lastPeak.Value * (1m - (decimal)PullbackPct / 100m))
                _pullbackSeen = true;

            // Re-break felfelé a pullback után
            if (_pullbackSeen && colorFlipUp)
            {
                bool passesScaleFilter = !Use200DFilter || candle.Close > ma200D.Value;
                if (passesScaleFilter)
                {
                    _canAdd         = false;
                    _pullbackSeen   = false;
                    _entryPriceCore = candle.Close;
                    _nextTpIndex    = 1;
                    _trailBase      = null;
                    return TradeSignal.Buy;
                }
            }
        }

        // ── Belépés: bearből bullba (piros → zöld) ────────────────────────────
        bool passesFilter = !Use200DFilter || candle.Close > ma200D.Value;
        if (colorFlipUp && passesFilter && portfolio.OpenTrade == null)
        {
            _entryPriceCore = candle.Close;
            _nextTpIndex    = 1;
            _canAdd         = false;
            _pullbackSeen   = false;
            _trailBase      = null;
            return TradeSignal.Buy;
        }

        return TradeSignal.Hold;
    }
}
