namespace MyBot.Backtesting.Indicators;

/// <summary>
/// Moving average helper methods (SMA and EMA).
/// Delegates to <see cref="SMA"/> and <see cref="EMA"/> for the actual calculations.
/// Returns 0 for positions where there is insufficient data (matches Pine Script behavior).
/// </summary>
public static class MovingAverages
{
    /// <summary>
    /// Calculates the Simple Moving Average.
    /// Returns 0 for positions where there is insufficient data.
    /// </summary>
    public static List<decimal> SMA(List<decimal> values, int period)
    {
        var nullable = Indicators.SMA.Calculate(values, period);
        return nullable.Select(v => v ?? 0m).ToList();
    }

    /// <summary>
    /// Calculates the Exponential Moving Average.
    /// Returns 0 for positions where there is insufficient data.
    /// </summary>
    public static List<decimal> EMA(List<decimal> values, int period)
    {
        var nullable = Indicators.EMA.Calculate(values, period);
        return nullable.Select(v => v ?? 0m).ToList();
    }
}
