using MyBot.Backtesting.Models;
using MyBot.Backtesting.Strategies;

namespace MyBot.Backtesting.Engine;

/// <summary>
/// The main backtesting engine. Iterates through historical candle data chronologically,
/// calls the strategy for each candle, executes signals, and tracks portfolio performance.
/// </summary>
public class BacktestEngine
{
    /// <summary>
    /// Runs a backtest for the given strategy on historical data.
    /// </summary>
    /// <param name="strategy">The trading strategy to test.</param>
    /// <param name="historicalData">Ordered list of historical OHLCV candles.</param>
    /// <param name="initialBalance">Starting cash balance.</param>
    /// <param name="config">Backtest configuration.</param>
    /// <returns>A <see cref="BacktestResult"/> with all trade and performance data.</returns>
    public BacktestResult RunBacktest(
        IBacktestStrategy strategy,
        List<OHLCVCandle> historicalData,
        decimal initialBalance,
        BacktestConfig config)
    {
        if (historicalData == null || historicalData.Count == 0)
            throw new ArgumentException("Historical data cannot be empty.", nameof(historicalData));
        if (initialBalance <= 0)
            throw new ArgumentException("Initial balance must be positive.", nameof(initialBalance));

        strategy.Initialize(new StrategyParameters());

        var portfolio = new VirtualPortfolio(initialBalance);
        var simulator = new OrderSimulator(config);
        var equityCurve = new List<PortfolioSnapshot>();
        var symbol = historicalData[0].Symbol;

        // Iterate chronologically - no look-ahead bias
        for (var i = 0; i < historicalData.Count; i++)
        {
            var candle = historicalData[i];
            var pastCandles = historicalData.Take(i + 1).ToList(); // only candles up to current

            // Enforce hard stop-loss before asking strategy for a signal
            var signal = EnforceHardStopLoss(candle, portfolio, symbol, config)
                         ?? strategy.OnCandle(candle, portfolio, pastCandles);

            ExecuteSignal(signal, candle, portfolio, simulator, symbol);

            // Record equity curve snapshot
            equityCurve.Add(new PortfolioSnapshot
            {
                Timestamp = candle.Timestamp,
                TotalValue = portfolio.GetTotalValue(symbol, candle.Close),
                CashBalance = portfolio.CashBalance,
                PositionValue = portfolio.Holdings.TryGetValue(symbol, out var qty) ? qty * candle.Close : 0m
            });
        }

        // Close any open position at last candle close price
        if (portfolio.OpenTrade != null && portfolio.Holdings.TryGetValue(symbol, out var finalQty) && finalQty > 0)
        {
            var lastCandle = historicalData[^1];
            var sellPrice = simulator.SimulateMarketSell(lastCandle);
            var fee = simulator.CalculateTakerFee(sellPrice * finalQty);
            portfolio.ExecuteSell(symbol, sellPrice, finalQty, fee, lastCandle.Timestamp);
        }

        var finalBalance = portfolio.CashBalance + portfolio.Holdings.Values.Sum(q => q * historicalData[^1].Close);
        var metrics = CalculateMetrics(portfolio.Trades, initialBalance, finalBalance, historicalData[0].Timestamp, historicalData[^1].Timestamp, equityCurve);

        return new BacktestResult
        {
            StrategyName = strategy.Name,
            Symbol = symbol,
            Timeframe = string.Empty,
            StartDate = historicalData[0].Timestamp,
            EndDate = historicalData[^1].Timestamp,
            InitialBalance = initialBalance,
            FinalBalance = finalBalance,
            Metrics = metrics,
            Trades = portfolio.Trades,
            EquityCurve = equityCurve,
            Config = config
        };
    }

    private static TradeSignal? EnforceHardStopLoss(
        OHLCVCandle candle,
        VirtualPortfolio portfolio,
        string symbol,
        BacktestConfig config)
    {
        if (portfolio.OpenTrade == null || config.MaxLossPerTradePercent <= 0)
            return null;

        var currentPrice = candle.Close;
        var entryPrice = portfolio.OpenTrade.EntryPrice;
        var unrealizedLossPct = (currentPrice - entryPrice) / entryPrice;

        if (unrealizedLossPct < -config.MaxLossPerTradePercent)
            return TradeSignal.Sell;

        return null;
    }

    private static void ExecuteSignal(
        TradeSignal signal,
        OHLCVCandle candle,
        VirtualPortfolio portfolio,
        OrderSimulator simulator,
        string symbol)
    {
        if (signal == TradeSignal.Buy && portfolio.OpenTrade == null)
        {
            var buyPrice = simulator.SimulateMarketBuy(candle);
            var totalValue = portfolio.GetTotalValue(symbol, candle.Close);
            var quantity = simulator.CalculateBuyQuantity(portfolio.CashBalance, buyPrice, totalValue);
            if (quantity > 0 && portfolio.CanBuy(buyPrice, quantity, simulator.CalculateTakerFee(1m)))
            {
                var fee = simulator.CalculateTakerFee(buyPrice * quantity);
                portfolio.ExecuteBuy(symbol, buyPrice, quantity, fee, candle.Timestamp);
            }
        }
        else if (signal == TradeSignal.Sell && portfolio.OpenTrade != null)
        {
            if (portfolio.Holdings.TryGetValue(symbol, out var qty) && qty > 0)
            {
                var sellPrice = simulator.SimulateMarketSell(candle);
                var fee = simulator.CalculateTakerFee(sellPrice * qty);
                portfolio.ExecuteSell(symbol, sellPrice, qty, fee, candle.Timestamp);
            }
        }
    }

    private static PerformanceMetrics CalculateMetrics(
        List<Trade> trades,
        decimal initialBalance,
        decimal finalBalance,
        DateTime startDate,
        DateTime endDate,
        List<PortfolioSnapshot> equityCurve)
    {
        var duration = endDate - startDate;
        var totalReturn = finalBalance - initialBalance;
        var totalReturnPct = initialBalance > 0 ? (totalReturn / initialBalance) * 100m : 0m;

        // Annualized return
        var years = (decimal)duration.TotalDays / 365m;
        var annualizedReturn = years > 0 && initialBalance > 0
            ? (decimal)(Math.Pow((double)(finalBalance / initialBalance), (double)(1m / years)) - 1) * 100m
            : 0m;

        // Max drawdown
        var (maxDrawdown, maxDrawdownPct) = CalculateMaxDrawdown(equityCurve);

        // Trade stats
        var completedTrades = trades.Where(t => t.ExitTime.HasValue).ToList();
        var winningTrades = completedTrades.Where(t => t.ProfitLoss > 0).ToList();
        var losingTrades = completedTrades.Where(t => t.ProfitLoss <= 0).ToList();

        var grossProfit = winningTrades.Sum(t => t.ProfitLoss);
        var grossLoss = Math.Abs(losingTrades.Sum(t => t.ProfitLoss));

        var avgHoldingHours = completedTrades.Count > 0
            ? completedTrades.Average(t => (t.ExitTime!.Value - t.EntryTime).TotalHours)
            : 0;

        // Sharpe ratio (simplified, using daily returns)
        var sharpeRatio = CalculateSharpeRatio(equityCurve);
        var sortinoRatio = CalculateSortinoRatio(equityCurve);

        return new PerformanceMetrics
        {
            TotalReturn = totalReturn,
            TotalReturnPercentage = totalReturnPct,
            AnnualizedReturn = annualizedReturn,
            MaxDrawdown = maxDrawdown,
            MaxDrawdownPercentage = maxDrawdownPct,
            SharpeRatio = sharpeRatio,
            SortinoRatio = sortinoRatio,
            TotalTrades = completedTrades.Count,
            WinningTrades = winningTrades.Count,
            LosingTrades = losingTrades.Count,
            WinRate = completedTrades.Count > 0 ? (decimal)winningTrades.Count / completedTrades.Count : 0m,
            ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? decimal.MaxValue : 0m,
            AverageWin = winningTrades.Count > 0 ? winningTrades.Average(t => t.ProfitLoss) : 0m,
            AverageLoss = losingTrades.Count > 0 ? Math.Abs(losingTrades.Average(t => t.ProfitLoss)) : 0m,
            LargestWin = winningTrades.Count > 0 ? winningTrades.Max(t => t.ProfitLoss) : 0m,
            LargestLoss = losingTrades.Count > 0 ? Math.Abs(losingTrades.Min(t => t.ProfitLoss)) : 0m,
            BacktestDuration = duration,
            AverageHoldingPeriodHours = (decimal)avgHoldingHours
        };
    }

    private static (decimal MaxDrawdown, decimal MaxDrawdownPct) CalculateMaxDrawdown(List<PortfolioSnapshot> equityCurve)
    {
        if (equityCurve.Count == 0) return (0m, 0m);

        var peak = equityCurve[0].TotalValue;
        var maxDrawdown = 0m;
        var maxDrawdownPct = 0m;

        foreach (var snapshot in equityCurve)
        {
            if (snapshot.TotalValue > peak) peak = snapshot.TotalValue;
            var drawdown = peak - snapshot.TotalValue;
            var drawdownPct = peak > 0 ? (drawdown / peak) * 100m : 0m;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            if (drawdownPct > maxDrawdownPct) maxDrawdownPct = drawdownPct;
        }

        return (maxDrawdown, maxDrawdownPct);
    }

    private static decimal CalculateSharpeRatio(List<PortfolioSnapshot> equityCurve)
    {
        if (equityCurve.Count < 2) return 0m;

        var returns = new List<decimal>();
        for (var i = 1; i < equityCurve.Count; i++)
        {
            if (equityCurve[i - 1].TotalValue > 0)
                returns.Add((equityCurve[i].TotalValue - equityCurve[i - 1].TotalValue) / equityCurve[i - 1].TotalValue);
        }

        if (returns.Count == 0) return 0m;

        var avg = returns.Average();
        var variance = returns.Average(r => (r - avg) * (r - avg));
        var stdDev = (decimal)Math.Sqrt((double)variance);

        return stdDev > 0 ? avg / stdDev * (decimal)Math.Sqrt(252) : 0m;
    }

    private static decimal CalculateSortinoRatio(List<PortfolioSnapshot> equityCurve)
    {
        if (equityCurve.Count < 2) return 0m;

        var returns = new List<decimal>();
        for (var i = 1; i < equityCurve.Count; i++)
        {
            if (equityCurve[i - 1].TotalValue > 0)
                returns.Add((equityCurve[i].TotalValue - equityCurve[i - 1].TotalValue) / equityCurve[i - 1].TotalValue);
        }

        if (returns.Count == 0) return 0m;

        var avg = returns.Average();
        var negativeReturns = returns.Where(r => r < 0).ToList();

        if (negativeReturns.Count == 0) return avg > 0 ? decimal.MaxValue : 0m;

        var downVariance = negativeReturns.Average(r => r * r);
        var downStdDev = (decimal)Math.Sqrt((double)downVariance);

        return downStdDev > 0 ? avg / downStdDev * (decimal)Math.Sqrt(252) : 0m;
    }
}
