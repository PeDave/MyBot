using System.Collections.Concurrent;
using MyBot.WebDashboard.Models.Auth;
using MyBot.WebDashboard.Models.Signals;

namespace MyBot.WebDashboard.Services.Signals;

public sealed class SignalService
{
    private readonly ConcurrentQueue<TradingSignal> _signals = new();
    private readonly Dictionary<SubscriptionPlan, TimeSpan> _delays = new()
    {
        [SubscriptionPlan.Free] = TimeSpan.FromMinutes(30),
        [SubscriptionPlan.Pro] = TimeSpan.FromMinutes(5),
        [SubscriptionPlan.ProPlus] = TimeSpan.Zero,
        [SubscriptionPlan.Admin] = TimeSpan.Zero
    };

    public TradingSignal CreateSignal(CreateSignalRequest request)
    {
        var signal = new TradingSignal(
            Guid.NewGuid(),
            request.Symbol.ToUpperInvariant(),
            request.TimeHorizon,
            request.Direction,
            request.EntryPrice,
            request.StopLoss,
            request.TakeProfit,
            request.StrategyTag,
            request.Scenario,
            DateTimeOffset.UtcNow);

        _signals.Enqueue(signal);
        return signal;
    }

    public IReadOnlyList<VisibleSignal> GetSignalsForPlan(SubscriptionPlan plan, int limit = 50)
    {
        var delay = _delays[plan];
        var now = DateTimeOffset.UtcNow;

        return _signals
            .Reverse()
            .Take(limit)
            .Select(signal =>
            {
                var visibleAt = signal.CreatedAtUtc + delay;
                var isDelayed = visibleAt > now;

                return new VisibleSignal(
                    signal.Id,
                    signal.Symbol,
                    signal.TimeHorizon,
                    signal.Direction,
                    signal.EntryPrice,
                    signal.StopLoss,
                    signal.TakeProfit,
                    signal.StrategyTag,
                    signal.Scenario,
                    signal.CreatedAtUtc,
                    visibleAt,
                    isDelayed,
                    plan);
            })
            .Where(s => !s.IsDelayed)
            .ToList();
    }
}
