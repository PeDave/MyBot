using System.Globalization;
using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.SmartMoney;

/// <summary>
/// Smart Money Concept strategy combining Fair Value Gaps and Liquidity Sweeps for swing entries.
///
/// Entry Logic:
///   1. Detect unfilled FVGs (institutional imbalances).
///   2. Confirm a recent liquidity sweep in the same direction.
///   3. Enter when current price is inside the FVG zone.
///
/// Exit Logic:
///   - Take Profit: next liquidity zone (swing high/low) or risk × RR multiple.
///   - Stop Loss: opposite edge of the FVG.
///   - Time exit: close after 48 hours (swing trade).
/// </summary>
public class FvgLiquiditySwingStrategy : IBacktestStrategy
{
    /// <inheritdoc/>
    public string Name => "FVG + Liquidity Swing";

    /// <inheritdoc/>
    public string Description => "SMC strategy using Fair Value Gaps and liquidity sweeps for swing entries";

    private List<FairValueGap> _fvgs = new();
    private List<LiquidityZone> _liquidityZones = new();
    private decimal _riskRewardRatio = 2.0m;
    private int _swingLookback = 10;
    private decimal _fvgMinGapPercent = 0.1m;

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters)
    {
        _riskRewardRatio = parameters.Get("RiskRewardRatio", 2.0m);
        _swingLookback = parameters.Get("SwingLookback", 10);
        _fvgMinGapPercent = parameters.Get("FvgMinGapPercent", 0.1m);
    }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < 50) return TradeSignal.Hold;

        // Refresh detections on first run and every 20 candles
        if (_fvgs.Count == 0 || historicalCandles.Count % 20 == 0)
        {
            _fvgs = FVGDetector.DetectFVGs(historicalCandles, _fvgMinGapPercent);
            FVGDetector.UpdateFVGFillStatus(_fvgs, historicalCandles);

            _liquidityZones = LiquidityDetector.DetectSwingPoints(historicalCandles, _swingLookback, _swingLookback);
            LiquidityDetector.DetectLiquiditySweeps(_liquidityZones, historicalCandles);
        }

        // ── ENTRY ────────────────────────────────────────────────────────────────
        if (portfolio.OpenTrade == null)
        {
            // Recent unfilled FVGs (skip the last 5 candles to avoid look-ahead)
            var recentFvgs = _fvgs
                .Where(f => !f.IsFilled && f.Index < historicalCandles.Count - 5)
                .OrderByDescending(f => f.Index)
                .Take(3);

            // Recent liquidity sweeps
            var recentSweeps = _liquidityZones
                .Where(z => z.IsSwept && z.SweptAt.HasValue)
                .OrderByDescending(z => z.Index)
                .Take(2)
                .ToList();

            foreach (var fvg in recentFvgs)
            {
                bool inFvgZone = candle.Low <= fvg.GapTop && candle.High >= fvg.GapBottom;
                if (!inFvgZone) continue;

                // Need a recent liquidity sweep in the same direction as the FVG
                var relevantSweep = recentSweeps.FirstOrDefault(s =>
                    (fvg.IsBullish && !s.IsHigh) || (!fvg.IsBullish && s.IsHigh));
                if (relevantSweep == null) continue;

                if (fvg.IsBullish)
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"[FVG-LIQ] BUY at {candle.Close:F2} | FVG {fvg.GapBottom:F2}-{fvg.GapTop:F2}"));
                    return TradeSignal.Buy;
                }
                else
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"[FVG-LIQ] SELL at {candle.Close:F2} | FVG {fvg.GapBottom:F2}-{fvg.GapTop:F2}"));
                    return TradeSignal.Sell;
                }
            }
        }

        // ── EXIT ─────────────────────────────────────────────────────────────────
        if (portfolio.OpenTrade != null)
        {
            var holdTime = (candle.Timestamp - portfolio.OpenTrade.EntryTime).TotalHours;
            if (holdTime > 48)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[FVG-LIQ] Time exit after {holdTime:F1}h"));
                return TradeSignal.Sell;
            }
        }

        return TradeSignal.Hold;
    }
}
