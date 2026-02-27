namespace MyBot.WebDashboard.Models.Auth;

public sealed class AppUser
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public required SubscriptionPlan Plan { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
