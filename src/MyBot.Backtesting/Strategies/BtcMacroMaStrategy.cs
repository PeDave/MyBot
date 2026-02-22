using System.Globalization;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies;

/// <summary>
/// BTC Macro MA Trend Strategy – Bull Market Support Band alapú stratégia.
///
/// Belépési logika (LONG):
///   Piros → zöld színváltás: az ár a bear zónából (SMA20W/EMA21W alatt)
///   átlép a bull zónába (sáv belsejébe vagy fölé).
///   Opcionális filter: csak 200D SMA felett (Use200DFilter kapcsoló).
///
/// Kilépési logika:
///   - Trendváltás: zöld → piros színváltás (ár visszaesik bandBot alá).
///   - Trailing stop: az utolsó csúcstól TrailPct%-os esés esetén exit.
///
/// Multi-timeframe aggregáció:
///   A stratégia a beérkező gyertyákat (órai vagy napi) napi és heti
///   aggregátumokká alakítja, majd ezekből számítja az indikátorokat.
/// </summary>
public class BtcMacroMaStrategy : IBacktestStrategy
{
    // ── Konfiguráció ──────────────────────────────────────────────────────────

    /// <summary>200D SMA szűrő: csak akkor lép be, ha az ár a 200 napos SMA felett van.</summary>
    public bool Use200DFilter { get; set; } = false;

    /// <summary>Trailing stop használata.</summary>
    public bool UseTrailing { get; set; } = true;

    /// <summary>Trailing stop százalék: a legutóbbi csúcstól való maximális esés.</summary>
    public double TrailPct { get; set; } = 5.0;

    // ── Állapotváltozók ───────────────────────────────────────────────────────

    /// <summary>Trailing stop alapja: az eddigi legmagasabb High nyitott pozíció esetén.</summary>
    private decimal? _trailBase;

    // ── Interfész implementáció ───────────────────────────────────────────────

    /// <inheritdoc/>
    public string Name => "BTC Macro MA Trend (Bull Band)";

    /// <inheritdoc/>
    public string Description =>
        "Bull Market Support Band (SMA20W + EMA21W) alapú BTC trend stratégia. " +
        "Belépés: piros→zöld színváltáskor (bear zónából bull zónába), " +
        "kilépés: zöld→piros váltáskor vagy trailing stop triggerésekor.";

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        Use200DFilter = parameters.Get("Use200DFilter", false);
        UseTrailing   = parameters.Get("UseTrailing", true);
        TrailPct      = parameters.Get("TrailPct", 5.0);
        _trailBase    = null;
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        // Minimális adatigény: 200 napi gyertya (200D SMA) + 21 heti gyertya (EMA21W)
        // 200 nap ≈ 4800 óra, de napi bemenetnél elég a 200 darab.
        if (historicalCandles.Count < 50)
            return TradeSignal.Hold;

        // ── Multi-timeframe aggregáció ────────────────────────────────────────
        var dailyCandles  = AggregateToDailyCandles(historicalCandles);
        var weeklyCandles = AggregateToWeeklyCandles(historicalCandles);

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
        var bandTop     = Math.Max(sma20W.Value, ema21W.Value);
        var bandBot     = Math.Min(sma20W.Value, ema21W.Value);
        var bandBotPrev = Math.Min(sma20WPrev.Value, ema21WPrev.Value);

        // Előző napi close az előző napi gyertya Close értéke
        if (dailyCandles.Count < 2)
            return TradeSignal.Hold;
        var prevDailyClose = dailyCandles[^2].Close;

        // ── Zóna meghatározása ────────────────────────────────────────────────
        // Előző bar: bear zónában volt-e (bandBot alatt)?
        bool wasBelowPrev = prevDailyClose < bandBotPrev;

        // Jelenlegi bar zónái
        bool isBelowNow = candle.Close < bandBot;
        bool isInBandOrAbove = candle.Close >= bandBot;   // sávban vagy fölötte

        // ── Színváltás detektálás ─────────────────────────────────────────────
        // Piros → zöld: előző bar bear zónában, jelenlegi sávban vagy fölötte
        bool colorFlipUp   = wasBelowPrev && isInBandOrAbove;
        // Zöld → piros: előző bar NEM bear zónában, jelenlegi bear zónában
        bool colorFlipDown = !wasBelowPrev && isBelowNow;

        // ── Trailing stop kezelése ────────────────────────────────────────────
        if (UseTrailing && portfolio.OpenTrade != null)
        {
            _trailBase = _trailBase == null
                ? candle.High
                : Math.Max(_trailBase.Value, candle.High);

            var trailLine = _trailBase.Value * (1.0m - (decimal)TrailPct / 100m);
            if (candle.Close < trailLine)
            {
                _trailBase = null;
                return TradeSignal.Sell;
            }
        }

        // ── Kilépés: trendváltás (zöld → piros) ──────────────────────────────
        if (portfolio.OpenTrade != null && colorFlipDown)
        {
            _trailBase = null;
            return TradeSignal.Sell;
        }

        // ── Belépés: bearből bullba (piros → zöld) ────────────────────────────
        bool passesFilter = !Use200DFilter || candle.Close > ma200D.Value;
        if (colorFlipUp && passesFilter && portfolio.OpenTrade == null)
        {
            _trailBase = null;
            return TradeSignal.Buy;
        }

        return TradeSignal.Hold;
    }

    // ── Multi-timeframe aggregáció ────────────────────────────────────────────

    /// <summary>
    /// Aggregálja a bemeneti gyertyákat napi gyertyákká.
    /// Ha a bemenet már napi (egy gyertya/nap), az eredmény változatlan.
    /// </summary>
    internal static List<OHLCVCandle> AggregateToDailyCandles(List<OHLCVCandle> candles)
    {
        return candles
            .GroupBy(c => c.Timestamp.Date)
            .OrderBy(g => g.Key)
            .Select(g => new OHLCVCandle
            {
                Timestamp = g.Key,
                Open      = g.First().Open,
                High      = g.Max(c => c.High),
                Low       = g.Min(c => c.Low),
                Close     = g.Last().Close,
                Volume    = g.Sum(c => c.Volume),
                Symbol    = g.First().Symbol,
                Exchange  = g.First().Exchange
            })
            .ToList();
    }

    /// <summary>
    /// Aggregálja a bemeneti gyertyákat heti gyertyákká (ISO hét szerint csoportosítva).
    /// </summary>
    internal static List<OHLCVCandle> AggregateToWeeklyCandles(List<OHLCVCandle> candles)
    {
        return candles
            .GroupBy(c => GetIsoWeekKey(c.Timestamp))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var sorted = g.OrderBy(c => c.Timestamp).ToList();
                return new OHLCVCandle
                {
                    Timestamp = sorted.First().Timestamp,
                    Open      = sorted.First().Open,
                    High      = sorted.Max(c => c.High),
                    Low       = sorted.Min(c => c.Low),
                    Close     = sorted.Last().Close,
                    Volume    = sorted.Sum(c => c.Volume),
                    Symbol    = sorted.First().Symbol,
                    Exchange  = sorted.First().Exchange
                };
            })
            .ToList();
    }

    private static (int Year, int Week) GetIsoWeekKey(DateTime dt)
    {
        var week = ISOWeek.GetWeekOfYear(dt);
        var year = ISOWeek.GetYear(dt);
        return (year, week);
    }
}
