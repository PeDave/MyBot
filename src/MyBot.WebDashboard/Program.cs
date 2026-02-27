using MyBot.Core.Interfaces;
using MyBot.Exchanges.Bitget;
using MyBot.Exchanges.BingX;
using MyBot.Exchanges.Bybit;
using MyBot.Exchanges.Mexc;
using MyBot.WebDashboard.Models.Auth;
using MyBot.WebDashboard.Models.Signals;
using MyBot.WebDashboard.Services;
using MyBot.WebDashboard.Services.Auth;
using MyBot.WebDashboard.Services.Automation;
using MyBot.WebDashboard.Services.Signals;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<PriceService>();
builder.Services.AddSingleton<PortfolioService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<SignalService>();
builder.Services.AddSingleton<AutomationService>();

// ─── Exchange Wrappers ────────────────────────────────────────────────────────
builder.Services.AddSingleton<List<IExchangeWrapper>>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var exchanges = new List<IExchangeWrapper>();

    var bitgetSection = config.GetSection("ExchangeSettings:Bitget");
    if (!string.IsNullOrEmpty(bitgetSection["ApiKey"]))
    {
        exchanges.Add(new BitgetWrapper(
            bitgetSection["ApiKey"]!,
            bitgetSection["ApiSecret"]!,
            bitgetSection["Passphrase"]!,
            loggerFactory.CreateLogger<BitgetWrapper>()));
    }

    var bingxSection = config.GetSection("ExchangeSettings:BingX");
    if (!string.IsNullOrEmpty(bingxSection["ApiKey"]))
    {
        exchanges.Add(new BingXWrapper(
            bingxSection["ApiKey"]!,
            bingxSection["ApiSecret"]!,
            loggerFactory.CreateLogger<BingXWrapper>()));
    }

    var mexcSection = config.GetSection("ExchangeSettings:Mexc");
    if (!string.IsNullOrEmpty(mexcSection["ApiKey"]))
    {
        exchanges.Add(new MexcWrapper(
            mexcSection["ApiKey"]!,
            mexcSection["ApiSecret"]!,
            loggerFactory.CreateLogger<MexcWrapper>()));
    }

    var bybitSection = config.GetSection("ExchangeSettings:Bybit");
    if (!string.IsNullOrEmpty(bybitSection["ApiKey"]))
    {
        exchanges.Add(new BybitWrapper(
            bybitSection["ApiKey"]!,
            bybitSection["ApiSecret"]!,
            loggerFactory.CreateLogger<BybitWrapper>()));
    }

    return exchanges;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseStaticFiles();

// ─── API Endpoints ────────────────────────────────────────────────────────────
app.MapPost("/api/auth/register", (RegisterRequest req, AuthService auth) =>
{
    var (ok, error) = auth.Register(req);
    return ok ? Results.Ok(new { message = "Sikeres regisztráció." }) : Results.BadRequest(new { error });
});

app.MapPost("/api/auth/login", (LoginRequest req, AuthService auth) =>
{
    var result = auth.Login(req);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

app.MapGet("/api/account/me", (HttpContext ctx, AuthService auth) =>
{
    var user = GetUserFromBearer(ctx, auth);
    return user is null ? Results.Unauthorized() : Results.Ok(user);
});

app.MapGet("/api/portfolio", async (PortfolioService service) =>
{
    return Results.Ok(await service.GetPortfolioAsync());
});

app.MapGet("/api/portfolio/overview", async (PortfolioService service) =>
{
    return Results.Ok(await service.GetOverviewAsync());
});

app.MapGet("/api/portfolio/exchanges", async (PortfolioService service) =>
{
    return Results.Ok(await service.GetExchangeSummariesAsync());
});

app.MapGet("/api/portfolio/exchange/{name}", async (string name, PortfolioService service) =>
{
    try
    {
        return Results.Ok(await service.GetExchangeDetailsAsync(name));
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapPost("/api/signals", (HttpContext ctx, CreateSignalRequest req, AuthService auth, SignalService signals) =>
{
    var user = GetUserFromBearer(ctx, auth);
    if (user is null)
        return Results.Unauthorized();

    if (user.Plan is not SubscriptionPlan.Admin and not SubscriptionPlan.ProPlus)
        return Results.Forbid();

    var created = signals.CreateSignal(req);
    return Results.Ok(created);
});

app.MapGet("/api/signals", (HttpContext ctx, AuthService auth, SignalService signals) =>
{
    var user = GetUserFromBearer(ctx, auth);
    if (user is null)
        return Results.Unauthorized();

    return Results.Ok(signals.GetSignalsForPlan(user.Plan));
});

app.MapPost("/api/automation/events", async (HttpRequest req, AutomationService automation) =>
{
    using var reader = new StreamReader(req.Body);
    var payload = await reader.ReadToEndAsync();

    var source = req.Headers["x-source"].FirstOrDefault() ?? "unknown";
    var eventType = req.Headers["x-event-type"].FirstOrDefault() ?? "generic";

    return Results.Ok(automation.Ingest(source, eventType, payload));
});

app.MapGet("/api/automation/events", (AutomationService automation)
    => Results.Ok(automation.GetRecent()));

app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run("http://localhost:5000");

static UserContext? GetUserFromBearer(HttpContext context, AuthService auth)
{
    var header = context.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;

    var token = header["Bearer ".Length..].Trim();
    return auth.ValidateToken(token);
}
