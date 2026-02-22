using MyBot.Backtesting.Engine;
using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Strategies.SmartMoney;

/// <summary>
/// Smart Money Concept strategy combining Order Blocks and Change of Character (CHOCH).
///
/// Entry Logic:
///   1. Detect a CHOCH (trend-reversal signal).
///   2. Identify the nearest Order Block in the new trend direction.
///   3. Enter when price retraces into the OB zone.
///
/// Exit Logic:
///   - Time exit: close after 72 hours.
/// </summary>
public class OrderBlockChochStrategy : IBacktestStrategy
{
    /// <inheritdoc/>
    public string Name => "Order Block + CHOCH";

    /// <inheritdoc/>
    public string Description => "SMC strategy using Order Blocks and Change of Character for trend reversals";

    private List<OrderBlock> _orderBlocks = new();
    private List<MarketStructure> _structures = new();
    private List<LiquidityZone> _liquidityZones = new();

    /// <inheritdoc/>
    public void Initialize(StrategyParameters parameters) { }

    /// <inheritdoc/>
    public TradeSignal OnCandle(OHLCVCandle candle, VirtualPortfolio portfolio, List<OHLCVCandle> historicalCandles)
    {
        if (historicalCandles.Count < 100) return TradeSignal.Hold;

        // Refresh detections on first run and every 10 candles
        if (_orderBlocks.Count == 0 || historicalCandles.Count % 10 == 0)
        {
            _orderBlocks = OrderBlockDetector.DetectOrderBlocks(historicalCandles);
            _liquidityZones = LiquidityDetector.DetectSwingPoints(historicalCandles);
            _structures = MarketStructureDetector.DetectStructureBreaks(historicalCandles, _liquidityZones);
        }

        // Most recent CHOCH
        var recentChoch = _structures
            .Where(s => s.Type == StructureBreak.BullishCHOCH || s.Type == StructureBreak.BearishCHOCH)
            .OrderByDescending(s => s.Index)
            .FirstOrDefault();

        if (recentChoch == null) return TradeSignal.Hold;

        // ── ENTRY ────────────────────────────────────────────────────────────────
        if (portfolio.OpenTrade == null)
        {
            // Order blocks in the 20-candle window before the CHOCH, matching direction
            var relevantOBs = _orderBlocks
                .Where(ob => ob.Index > recentChoch.Index - 20 && ob.Index < recentChoch.Index)
                .Where(ob =>
                    (recentChoch.Type == StructureBreak.BullishCHOCH && ob.IsBullish) ||
                    (recentChoch.Type == StructureBreak.BearishCHOCH && !ob.IsBullish))
                .OrderByDescending(ob => ob.Index)
                .ToList();

            if (relevantOBs.Count > 0)
            {
                var orderBlock = relevantOBs[0];
                bool inOBZone = candle.Low <= orderBlock.BlockTop && candle.High >= orderBlock.BlockBottom;

                if (inOBZone)
                {
                    if (recentChoch.Type == StructureBreak.BullishCHOCH)
                    {
                        Console.WriteLine(
                            $"[OB-CHOCH] BUY at OB {orderBlock.BlockBottom:F2}-{orderBlock.BlockTop:F2}");
                        return TradeSignal.Buy;
                    }
                    else
                    {
                        Console.WriteLine(
                            $"[OB-CHOCH] SELL at OB {orderBlock.BlockBottom:F2}-{orderBlock.BlockTop:F2}");
                        return TradeSignal.Sell;
                    }
                }
            }
        }

        // ── EXIT ─────────────────────────────────────────────────────────────────
        if (portfolio.OpenTrade != null)
        {
            var holdTime = (candle.Timestamp - portfolio.OpenTrade.EntryTime).TotalHours;
            if (holdTime > 72)
            {
                Console.WriteLine($"[OB-CHOCH] Time exit after {holdTime:F1}h");
                return TradeSignal.Sell;
            }
        }

        return TradeSignal.Hold;
    }
}
