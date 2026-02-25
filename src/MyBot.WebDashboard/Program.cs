using MyBot.Core.Interfaces;
using MyBot.Exchanges.Bitget;
using MyBot.Exchanges.BingX;
using MyBot.Exchanges.Mexc;
using MyBot.Exchanges.Bybit;
using MyBot.WebDashboard.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<PriceService>();
builder.Services.AddSingleton<PortfolioService>();

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
app.MapGet("/api/portfolio", async (PortfolioService service) =>
{
    return Results.Ok(await service.GetPortfolioAsync());
});

// 1️⃣ Portfolio Overview
app.MapGet("/api/portfolio/overview", async (PortfolioService service) =>
{
    return Results.Ok(await service.GetOverviewAsync());
});

// 2️⃣ Exchange Summary
app.MapGet("/api/portfolio/exchanges", async (PortfolioService service) =>
{
    return Results.Ok(await service.GetExchangeSummariesAsync());
});

// 3️⃣ Exchange Details
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

app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run("http://localhost:5000");
