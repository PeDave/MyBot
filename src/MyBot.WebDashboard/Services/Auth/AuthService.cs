using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MyBot.WebDashboard.Models.Auth;

namespace MyBot.WebDashboard.Services.Auth;

public sealed class AuthService
{
    private readonly ConcurrentDictionary<string, AppUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, UserContext> _tokens = new(StringComparer.Ordinal);

    public AuthService()
    {
        var adminEmail = "admin@labotkripto.com";
        var adminPassword = "ChangeMe123!";
        var admin = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            PasswordHash = HashPassword(adminPassword),
            Plan = SubscriptionPlan.Admin
        };

        _users[adminEmail] = admin;
    }

    public (bool Success, string? Error) Register(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return (false, "Email és jelszó kötelező.");

        var normalizedEmail = request.Email.Trim();

        if (_users.ContainsKey(normalizedEmail))
            return (false, "Ez az email már regisztrált.");

        if (!Enum.IsDefined(request.Plan) || request.Plan is SubscriptionPlan.ProPlus or SubscriptionPlan.Admin)
            return (false, "A választott csomag regisztrációkor nem elérhető.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = HashPassword(request.Password),
            Plan = request.Plan
        };

        _users[user.Email] = user;
        return (true, null);
    }

    public AuthResponse? Login(LoginRequest request)
    {
        if (!_users.TryGetValue(request.Email.Trim(), out var user))
            return null;

        if (!VerifyPassword(request.Password, user.PasswordHash))
            return null;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = new UserContext(user.Id, user.Email, user.Plan);

        return new AuthResponse(token, user.Email, user.Plan);
    }

    public UserContext? ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        _tokens.TryGetValue(token, out var user);
        return user;
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    private static bool VerifyPassword(string input, string hash)
        => string.Equals(HashPassword(input), hash, StringComparison.Ordinal);
}
