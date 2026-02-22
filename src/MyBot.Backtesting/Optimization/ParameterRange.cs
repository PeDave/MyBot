namespace MyBot.Backtesting.Optimization;

/// <summary>Defines a numeric range for a strategy parameter with a step size for grid search.</summary>
public class ParameterRange
{
    /// <summary>Minimum value (inclusive).</summary>
    public decimal Min { get; set; }
    /// <summary>Maximum value (inclusive).</summary>
    public decimal Max { get; set; }
    /// <summary>Step size between values.</summary>
    public decimal Step { get; set; }

    /// <summary>Enumerates all values in the range from <see cref="Min"/> to <see cref="Max"/> by <see cref="Step"/>.</summary>
    public IEnumerable<decimal> GetValues()
    {
        if (Step <= 0) throw new InvalidOperationException("Step must be positive.");
        for (var val = Min; val <= Max + Step / 2m; val += Step)
            yield return val;
    }
}

/// <summary>
/// A parameter grid mapping parameter names to their ranges for grid-search optimization.
/// <example>
/// <code>
/// var grid = new ParameterGrid
/// {
///     ["BollingerPeriod"] = new ParameterRange { Min = 10, Max = 30, Step = 5 },
///     ["StdDevMultiplier"] = new ParameterRange { Min = 1.5m, Max = 3.0m, Step = 0.5m }
/// };
/// </code>
/// </example>
/// </summary>
public class ParameterGrid : Dictionary<string, ParameterRange> { }
