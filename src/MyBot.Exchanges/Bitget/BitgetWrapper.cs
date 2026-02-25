using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bitget.Net.Clients;
using Bitget.Net.Enums;
using CryptoExchange.Net.Authentication;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BitgetOrderEntry = Bitget.Net.Objects.Models.V2.BitgetOrder;
using BitgetOrderSide = Bitget.Net.Enums.V2.OrderSide;
using BitgetOrderType = Bitget.Net.Enums.V2.OrderType;
using BitgetOrderStatus = Bitget.Net.Enums.V2.OrderStatus;
using BitgetTimeInForce = Bitget.Net.Enums.V2.TimeInForce;
using BitgetTradeSide = Bitget.Net.Enums.BitgetOrderSide;
using BitgetKlineInterval = Bitget.Net.Enums.V2.KlineInterval;

namespace MyBot.Exchanges.Bitget;

/// <summary>Wrapper for the Bitget cryptocurrency exchange using JK.Bitget.Net SDK.</summary>
public class BitgetWrapper : IExchangeWrapper, IDisposable
{
    private readonly BitgetRestClient _client;
    private readonly ILogger<BitgetWrapper> _logger;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _passphrase;
    private readonly HttpClient _httpClient;
    private const string _baseUrl = "https://api.bitget.com";

    public string ExchangeName => "Bitget";

    public BitgetWrapper(string apiKey, string apiSecret, string passphrase, ILogger<BitgetWrapper> logger)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _passphrase = passphrase;
        _logger = logger;
        _httpClient = new HttpClient();
        _client = new BitgetRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret, passphrase);
        });
    }

    public async Task<IEnumerable<UnifiedBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.Account.GetSpotBalancesAsync(ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get balances", result.Error?.Code?.ToString());

            return result.Data.Select(b => new UnifiedBalance
            {
                Asset = b.Asset,
                Available = b.Available,
                Locked = b.Frozen + b.Locked,
                Total = b.Available + b.Frozen + b.Locked,
                Exchange = ExchangeName,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balances from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<AccountBalances> GetAllAccountBalancesAsync(CancellationToken cancellationToken = default)
    {
        var result = new AccountBalances();

        try
        {
            _logger.LogInformation("Fetching all Bitget account balances using all-account-balance API...");

            // ────────────────────────────────────────────────────────────────────
            // 1️⃣ All Account Balance Overview (egyetlen API hívás!)
            // ────────────────────────────────────────────────────────────────────
            var endpoint = "/api/v2/account/all-account-balance";
            var request = CreateAuthenticatedRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Bitget all-account-balance failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<BitgetAllAccountBalanceResponse>(json);

            if (data?.Code == "00000" && data?.Data != null)
            {
                foreach (var account in data.Data)
                {
                    if (!decimal.TryParse(account.UsdtBalance, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var usdBalance) || usdBalance <= 0)
                        continue;

                    // USDT asset létrehozása (USDT = 1:1 USD)
                    var asset = new AssetBalance
                    {
                        Asset = "USDT",
                        Free = usdBalance,
                        Locked = 0,
                        UsdValue = usdBalance
                    };

                    // Account típus szerint besorolás
                    switch (account.AccountType.ToLowerInvariant())
                    {
                        case "spot":
                            result.Spot.Add(asset);
                            _logger.LogInformation("Bitget Spot: {Balance} USDT", usdBalance);
                            break;
                        case "futures":
                            result.UsdtMFutures.Add(asset);
                            _logger.LogInformation("Bitget Futures: {Balance} USDT", usdBalance);
                            break;
                        case "funding":
                            result.Spot.Add(asset); // Funding-ot Spot-hoz adjuk
                            _logger.LogInformation("Bitget Funding: {Balance} USDT", usdBalance);
                            break;
                        case "earn":
                            result.Earn.Add(asset);
                            _logger.LogInformation("Bitget Earn: {Balance} USDT", usdBalance);
                            break;
                    }
                }
            }
            else
            {
                _logger.LogWarning("Bitget all-account-balance returned code: {Code}, msg: {Msg}", data?.Code, data?.Msg);
            }

            // ────────────────────────────────────────────────────────────────────
            // 2️⃣ Bot Account Assets (futures + spot)
            // ────────────────────────────────────────────────────────────────────
            var futuresBotAssets = await GetBotAssetsAsync("futures", cancellationToken);
            result.Bot.AddRange(futuresBotAssets);
            _logger.LogInformation("Bitget Futures Bot: {Count} assets, Total USD: {Total}",
                futuresBotAssets.Count,
                futuresBotAssets.Sum(a => a.UsdValue));

            var spotBotAssets = await GetBotAssetsAsync("spot", cancellationToken);
            result.Bot.AddRange(spotBotAssets);
            _logger.LogInformation("Bitget Spot Bot: {Count} assets, Total USD: {Total}",
                spotBotAssets.Count,
                spotBotAssets.Sum(a => a.UsdValue));

            // ────────────────────────────────────────────────────────────────────
            // ÖSSZESÍTÉS
            // ────────────────────────────────────────────────────────────────────
            var totalSpot = result.Spot.Sum(a => a.UsdValue);
            var totalFutures = result.UsdtMFutures.Sum(a => a.UsdValue);
            var totalEarn = result.Earn.Sum(a => a.UsdValue);
            var totalBot = result.Bot.Sum(a => a.UsdValue);
            var grandTotal = totalSpot + totalFutures + totalEarn + totalBot;

            _logger.LogInformation(
                "Bitget TOTAL - Spot: ${Spot}, Futures: ${Futures}, Earn: ${Earn}, Bot: ${Bot}, GRAND TOTAL: ${GrandTotal}",
                totalSpot, totalFutures, totalEarn, totalBot, grandTotal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all account balances from {Exchange}", ExchangeName);
        }

        return result;
    }

    private async Task<List<AssetBalance>> GetBotAssetsAsync(string accountType, CancellationToken cancellationToken = default)
    {
        var balances = new List<AssetBalance>();
        try
        {
            var endpoint = $"/api/v2/account/bot-assets?accountType={accountType}";
            var request = CreateAuthenticatedRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                // Ha nincs bot account, az nem hiba
                if (errorContent.Contains("40007") || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("No {AccountType} Bot account found on Bitget", accountType);
                    return balances;
                }

                _logger.LogWarning("Bitget {AccountType} Bot assets failed: {StatusCode} - {Error}",
                    accountType, response.StatusCode, errorContent);
                return balances;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<BitgetBotAssetsResponse>(json);

            if (data?.Code == "00000" && data?.Data != null)
            {
                foreach (var asset in data.Data)
                {
                    if (string.IsNullOrEmpty(asset.Equity) ||
                        !decimal.TryParse(asset.Equity, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var equity) ||
                        equity <= 0)
                    {
                        continue;
                    }

                    var usdValue = decimal.TryParse(asset.UsdtValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var usd) ? usd : equity;
                    _ = decimal.TryParse(asset.Available, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var available);
                    _ = decimal.TryParse(asset.Frozen, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var frozen);

                    balances.Add(new AssetBalance
                    {
                        Asset = asset.Coin,
                        Free = available,
                        Locked = frozen,
                        UsdValue = usdValue
                    });

                    _logger.LogInformation("Bitget {AccountType} Bot - {Coin}: {Equity} (USD: {UsdValue})",
                        accountType, asset.Coin, equity, usdValue);
                }
            }
            else
            {
                _logger.LogWarning("Bitget {AccountType} Bot assets returned code: {Code}, msg: {Msg}",
                    accountType, data?.Code, data?.Msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch {AccountType} Bot assets from Bitget", accountType);
        }
        return balances;
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string endpoint)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(timestamp, method.Method, endpoint, "");
        var request = new HttpRequestMessage(method, _baseUrl + endpoint);
        request.Headers.Add("ACCESS-KEY", _apiKey);
        request.Headers.Add("ACCESS-SIGN", signature);
        request.Headers.Add("ACCESS-TIMESTAMP", timestamp);
        request.Headers.Add("ACCESS-PASSPHRASE", _passphrase);
        return request;
    }

    private string GenerateSignature(string timestamp, string method, string endpoint, string body)
    {
        var prehash = timestamp + method.ToUpper() + endpoint + body;
        var keyBytes = Encoding.UTF8.GetBytes(_apiSecret);
        var messageBytes = Encoding.UTF8.GetBytes(prehash);
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hashBytes);
    }

    private class BitgetAllAccountBalanceResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonPropertyName("data")]
        public List<AccountBalanceItem>? Data { get; set; }
    }

    private class AccountBalanceItem
    {
        [JsonPropertyName("accountType")]
        public string AccountType { get; set; } = string.Empty;

        [JsonPropertyName("usdtBalance")]
        public string UsdtBalance { get; set; } = string.Empty;
    }

    private class BitgetBotAssetsResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonPropertyName("data")]
        public List<BotAsset>? Data { get; set; }
    }

    private class BotAsset
    {
        [JsonPropertyName("coin")]
        public string Coin { get; set; } = string.Empty;

        [JsonPropertyName("available")]
        public string? Available { get; set; }

        [JsonPropertyName("equity")]
        public string? Equity { get; set; }

        [JsonPropertyName("frozen")]
        public string? Frozen { get; set; }

        [JsonPropertyName("bonus")]
        public string? Bonus { get; set; }

        [JsonPropertyName("usdtValue")]
        public string? UsdtValue { get; set; }
    }

    public async Task<UnifiedOrder> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity,
        decimal? price = null, TimeInForce timeInForce = TimeInForce.GoodTillCanceled, CancellationToken cancellationToken = default)
    {
        try
        {
            var bgSide = side == OrderSide.Buy ? BitgetOrderSide.Buy : BitgetOrderSide.Sell;
            var bgType = type == OrderType.Market ? BitgetOrderType.Market : BitgetOrderType.Limit;
            var bgTif = MapTimeInForce(timeInForce);

            var result = await _client.SpotApiV2.Trading.PlaceOrderAsync(
                symbol, bgSide, bgType, quantity, bgTif, price,
                ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to place order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId,
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = symbol,
                Side = side,
                Type = type,
                Quantity = quantity,
                Price = price,
                Status = OrderStatus.New,
                TimeInForce = timeInForce,
                CreateTime = DateTime.UtcNow,
                Exchange = ExchangeName
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing order on {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedOrder> CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.Trading.CancelOrderAsync(symbol, orderId, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to cancel order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId,
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = symbol,
                Status = OrderStatus.Canceled,
                Exchange = ExchangeName
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling order on {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedOrder> GetOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.Trading.GetOrderAsync(orderId, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order", result.Error?.Code?.ToString());

            var order = result.Data.FirstOrDefault()
                ?? throw new ExchangeException(ExchangeName, $"Order {orderId} not found");
            return MapOrder(order, symbol);
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<IEnumerable<UnifiedOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.Trading.GetOpenOrdersAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get open orders", result.Error?.Code?.ToString());

            return result.Data.Select(o => MapOrder(o, o.Symbol));
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting open orders from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<IEnumerable<UnifiedOrder>> GetOrderHistoryAsync(string? symbol = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.Trading.GetClosedOrdersAsync(symbol, limit: limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order history", result.Error?.Code?.ToString());

            return result.Data.Select(o => MapOrder(o, o.Symbol));
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order history from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedTicker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.ExchangeData.GetTickersAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get ticker", result.Error?.Code?.ToString());

            var t = result.Data.FirstOrDefault()
                ?? throw new ExchangeException(ExchangeName, $"Ticker for {symbol} not found");

            return new UnifiedTicker
            {
                Symbol = t.Symbol,
                LastPrice = t.LastPrice,
                BidPrice = t.BestBidPrice ?? 0,
                AskPrice = t.BestAskPrice ?? 0,
                High24h = t.HighPrice,
                Low24h = t.LowPrice,
                Volume24h = t.Volume,
                QuoteVolume24h = t.QuoteVolume,
                ChangePercent24h = t.ChangePercentage24H ?? 0,
                Exchange = ExchangeName,
                Timestamp = t.Timestamp
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ticker from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<UnifiedOrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.ExchangeData.GetOrderBookAsync(symbol, limit: depth, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order book", result.Error?.Code?.ToString());

            return new UnifiedOrderBook
            {
                Symbol = symbol,
                Bids = result.Data.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = result.Data.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = result.Data.Timestamp
            };
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order book from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public async Task<IEnumerable<UnifiedTrade>> GetRecentTradesAsync(string symbol, int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApiV2.ExchangeData.GetRecentTradesAsync(symbol, limit: limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get recent trades", result.Error?.Code?.ToString());

            return result.Data.Select(t => new UnifiedTrade
            {
                TradeId = t.TradeId,
                Symbol = t.Symbol,
                Price = t.Price,
                Quantity = t.Quantity,
                QuoteQuantity = t.Price * t.Quantity,
                TakerSide = t.Side == BitgetTradeSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                Timestamp = t.Timestamp,
                Exchange = ExchangeName
            });
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent trades from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    private UnifiedOrder MapOrder(BitgetOrderEntry order, string symbol) => new()
    {
        OrderId = order.OrderId,
        ClientOrderId = order.ClientOrderId,
        Symbol = string.IsNullOrEmpty(order.Symbol) ? symbol : order.Symbol,
        Side = order.Side == BitgetOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
        Type = order.OrderType == BitgetOrderType.Market ? OrderType.Market : OrderType.Limit,
        Status = MapOrderStatus(order.Status),
        Quantity = order.Quantity,
        QuantityFilled = order.QuantityFilled ?? 0,
        Price = order.Price,
        AveragePrice = order.AveragePrice,
        QuoteQuantityFilled = order.QuoteQuantityFilled,
        CreateTime = order.CreateTime,
        UpdateTime = order.UpdateTime,
        Exchange = ExchangeName
    };

    private static OrderStatus MapOrderStatus(BitgetOrderStatus status) => status switch
    {
        BitgetOrderStatus.New => OrderStatus.New,
        BitgetOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        BitgetOrderStatus.Filled => OrderStatus.Filled,
        BitgetOrderStatus.Canceled => OrderStatus.Canceled,
        BitgetOrderStatus.Rejected => OrderStatus.Rejected,
        _ => OrderStatus.Unknown
    };

    private static BitgetTimeInForce MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.ImmediateOrCancel => BitgetTimeInForce.ImmediateOrCancel,
        TimeInForce.FillOrKill => BitgetTimeInForce.FillOrKill,
        _ => BitgetTimeInForce.GoodTillCanceled
    };

    public async Task<IEnumerable<UnifiedKline>> GetKlinesAsync(string symbol, string timeframe, DateTime startTime, DateTime endTime, int limit = 200, CancellationToken cancellationToken = default)
    {
        try
        {
            var interval = timeframe switch
            {
                "1m" => BitgetKlineInterval.OneMinute,
                "5m" => BitgetKlineInterval.FiveMinutes,
                "15m" => BitgetKlineInterval.FifteenMinutes,
                "30m" => BitgetKlineInterval.ThirtyMinutes,
                "1h" => BitgetKlineInterval.OneHour,
                "4h" => BitgetKlineInterval.FourHours,
                "1d" => BitgetKlineInterval.OneDay,
                _ => throw new ArgumentException($"Unsupported timeframe: {timeframe}")
            };
            // Bitget history-candles endpoint only supports endTime + limit (not both startTime and endTime).
            // The Where clause filters out any candles before startTime that the API may return when
            // limit causes it to fetch further back than the requested range.
            var result = await _client.SpotApiV2.ExchangeData.GetHistoricalKlinesAsync(symbol, interval, endTime, limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get klines", result.Error?.Code?.ToString());
            return result.Data
                .Where(k => k.OpenTime >= startTime && k.OpenTime <= endTime)
                .Select(k => new UnifiedKline
                {
                    OpenTime = k.OpenTime,
                    Open = k.OpenPrice,
                    High = k.HighPrice,
                    Low = k.LowPrice,
                    Close = k.ClosePrice,
                    Volume = k.Volume,
                    QuoteVolume = k.QuoteVolume,
                    Symbol = symbol,
                    Exchange = ExchangeName
                });
        }
        catch (ExchangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting klines from {Exchange}", ExchangeName);
            throw new ExchangeException(ExchangeName, ex.Message, innerException: ex);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
    }
}
