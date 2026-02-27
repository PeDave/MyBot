using MyBot.WebDashboard.Models.Auth;

namespace MyBot.WebDashboard.Models.Signals;

public sealed record TradingSignal(
    Guid Id,
    string Symbol,
    string TimeHorizon,
    string Direction,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string StrategyTag,
    string Scenario,
    DateTimeOffset CreatedAtUtc);

public sealed record VisibleSignal(
    Guid Id,
    string Symbol,
    string TimeHorizon,
    string Direction,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string StrategyTag,
    string Scenario,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset VisibleAtUtc,
    bool IsDelayed,
    SubscriptionPlan ViewerPlan);

public sealed record CreateSignalRequest(
    string Symbol,
    string TimeHorizon,
    string Direction,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string StrategyTag,
    string Scenario);
