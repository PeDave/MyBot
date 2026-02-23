using System.Net.Http.Json;

namespace MyBot.WebDashboard.Services;

/// <summary>
/// Fetches current USD prices for crypto assets from Binance public API.
/// </summary>
public class PriceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PriceService> _logger;
    private readonly Dictionary<string, decimal> _priceCache = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;

    public PriceService(ILogger<PriceService> logger)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("https://api.binance.com") };
        _logger = logger;
    }

    public async Task<decimal> GetUsdPriceAsync(string asset)
    {
        if (asset == "USDT" || asset == "USDC" || asset == "BUSD")
            return 1.0m;

        // Cache for 30 seconds
        if ((DateTime.UtcNow - _lastCacheUpdate).TotalSeconds < 30 && _priceCache.ContainsKey(asset))
            return _priceCache[asset];

        try
        {
            var symbol = $"{asset}USDT";
            var response = await _httpClient.GetFromJsonAsync<BinanceTicker>($"/api/v3/ticker/price?symbol={symbol}");

            if (response != null && decimal.TryParse(response.Price, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                _priceCache[asset] = price;
                _lastCacheUpdate = DateTime.UtcNow;
                return price;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch price for {Asset}", asset);
        }

        return 0m;
    }

    private class BinanceTicker
    {
        public string Symbol { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
    }
}
