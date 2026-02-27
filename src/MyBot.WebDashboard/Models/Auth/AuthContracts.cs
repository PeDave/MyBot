namespace MyBot.WebDashboard.Models.Auth;

public sealed record RegisterRequest(string Email, string Password, SubscriptionPlan Plan = SubscriptionPlan.Free);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, string Email, SubscriptionPlan Plan);
public sealed record UserContext(Guid UserId, string Email, SubscriptionPlan Plan);
