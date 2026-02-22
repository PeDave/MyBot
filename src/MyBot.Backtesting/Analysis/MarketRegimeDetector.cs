using MyBot.Backtesting.Indicators;
using MyBot.Backtesting.Models;

namespace MyBot.Backtesting.Analysis;

/// <summary>Trend strength regime based on ADX values.</summary>
public enum TrendRegime
{
    /// <summary>ADX &gt; 25: strong directional trend.</summary>
    StrongTrending,
    /// <summary>ADX 20-25: weak or emerging trend.</summary>
    WeakTrending,
    /// <summary>ADX &lt; 20: sideways / range-bound market.</summary>
    Ranging
}

/// <summary>Volatility regime based on ATR as a percentage of price.</summary>
public enum VolatilityRegime
{
    /// <summary>ATR% &gt; 3%: high volatility.</summary>
    High,
    /// <summary>ATR% 1-3%: moderate volatility.</summary>
    Medium,
    /// <summary>ATR% &lt; 1%: low volatility.</summary>
    Low
}

/// <summary>Market phase based on price relative to SMA-200 and recent direction.</summary>
public enum MarketPhase
{
    /// <summary>Price above SMA-200 and trending up.</summary>
    Bull,
    /// <summary>Price below SMA-200 and trending down.</summary>
    Bear,
    /// <summary>Price oscillating around SMA-200 with no clear direction.</summary>
    Sideways
}

/// <summary>Combined market regime snapshot.</summary>
public class MarketRegime
{
    /// <summary>Trend strength regime.</summary>
    public TrendRegime TrendRegime { get; set; }
    /// <summary>Volatility regime.</summary>
    public VolatilityRegime VolatilityRegime { get; set; }
    /// <summary>Market phase.</summary>
    public MarketPhase MarketPhase { get; set; }
    /// <summary>Current ADX value (null if insufficient data).</summary>
    public decimal? Adx { get; set; }
    /// <summary>ATR as percentage of current price (null if insufficient data).</summary>
    public decimal? AtrPercent { get; set; }
    /// <summary>Recommended strategy name for the current regime.</summary>
    public string RecommendedStrategy { get; set; } = string.Empty;
}

/// <summary>
/// Detects market regime (trend, volatility, phase) from historical candle data.
/// </summary>
public static class MarketRegimeDetector
{
    private const int AdxPeriod = 14;
    private const int AtrPeriod = 14;
    private const int SmaPeriod = 200;

    /// <summary>
    /// Detects the current market regime at the given candle index.
    /// Returns null if there is insufficient data.
    /// </summary>
    /// <param name="candles">Historical candles (must be ordered by time).</param>
    /// <param name="currentIndex">Index of the candle to evaluate (inclusive).</param>
    public static MarketRegime? DetectRegime(List<OHLCVCandle> candles, int currentIndex)
    {
        if (candles == null || currentIndex < 0 || currentIndex >= candles.Count)
            return null;

        var slice = candles.Take(currentIndex + 1).ToList();
        if (slice.Count < AdxPeriod * 2 + 1)
            return null;

        var highs = slice.Select(c => c.High).ToList();
        var lows = slice.Select(c => c.Low).ToList();
        var closes = slice.Select(c => c.Close).ToList();

        // ADX
        var adxValues = ADX.Calculate(highs, lows, closes, AdxPeriod);
        var lastAdx = adxValues[^1];

        // ATR percentage
        var atrValues = ATR.Calculate(highs, lows, closes, AtrPeriod);
        var lastAtr = atrValues[^1];
        var currentPrice = closes[^1];
        var atrPercent = (lastAtr.HasValue && currentPrice > 0)
            ? (lastAtr.Value / currentPrice) * 100m
            : (decimal?)null;

        // SMA-200
        MarketPhase phase;
        if (slice.Count >= SmaPeriod)
        {
            var smaValues = SMA.Calculate(closes, SmaPeriod);
            var lastSma = smaValues[^1];
            if (lastSma.HasValue)
            {
                // Determine phase from SMA and recent price direction
                var recentSlice = closes.TakeLast(20).ToList();
                var recentChange = recentSlice.Count >= 2
                    ? recentSlice[^1] - recentSlice[0]
                    : 0m;

                if (currentPrice > lastSma.Value && recentChange > 0)
                    phase = MarketPhase.Bull;
                else if (currentPrice < lastSma.Value && recentChange < 0)
                    phase = MarketPhase.Bear;
                else
                    phase = MarketPhase.Sideways;
            }
            else
            {
                phase = MarketPhase.Sideways;
            }
        }
        else
        {
            // Not enough data for SMA-200; use shorter-term direction
            var recentSlice = closes.TakeLast(20).ToList();
            var recentChange = recentSlice.Count >= 2 ? recentSlice[^1] - recentSlice[0] : 0m;
            phase = recentChange > 0 ? MarketPhase.Bull : recentChange < 0 ? MarketPhase.Bear : MarketPhase.Sideways;
        }

        // Classify trend regime
        TrendRegime trendRegime;
        if (lastAdx == null)
        {
            trendRegime = TrendRegime.Ranging;
        }
        else if (lastAdx.Adx >= 25m)
        {
            trendRegime = TrendRegime.StrongTrending;
        }
        else if (lastAdx.Adx >= 20m)
        {
            trendRegime = TrendRegime.WeakTrending;
        }
        else
        {
            trendRegime = TrendRegime.Ranging;
        }

        // Classify volatility regime
        VolatilityRegime volatilityRegime;
        if (!atrPercent.HasValue)
        {
            volatilityRegime = VolatilityRegime.Medium;
        }
        else if (atrPercent.Value >= 3m)
        {
            volatilityRegime = VolatilityRegime.High;
        }
        else if (atrPercent.Value >= 1m)
        {
            volatilityRegime = VolatilityRegime.Medium;
        }
        else
        {
            volatilityRegime = VolatilityRegime.Low;
        }

        // Recommend strategy
        var recommendedStrategy = RecommendStrategy(trendRegime, volatilityRegime);

        return new MarketRegime
        {
            TrendRegime = trendRegime,
            VolatilityRegime = volatilityRegime,
            MarketPhase = phase,
            Adx = lastAdx?.Adx,
            AtrPercent = atrPercent,
            RecommendedStrategy = recommendedStrategy
        };
    }

    /// <summary>Returns the name of the recommended strategy for the given regime.</summary>
    public static string RecommendStrategy(TrendRegime trend, VolatilityRegime volatility)
    {
        return (trend, volatility) switch
        {
            (TrendRegime.StrongTrending, VolatilityRegime.High) => "MACD Trend",
            (TrendRegime.StrongTrending, _) => "Triple EMA + RSI",
            (TrendRegime.WeakTrending, VolatilityRegime.High) => "Triple EMA + RSI",
            (TrendRegime.WeakTrending, _) => "Support/Resistance Breakout",
            (TrendRegime.Ranging, VolatilityRegime.High) => "Bollinger Bands Breakout",
            _ => "Support/Resistance Breakout"
        };
    }
}
