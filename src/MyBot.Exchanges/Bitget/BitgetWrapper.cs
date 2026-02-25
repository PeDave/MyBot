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
using BitgetProductTypeV2 = Bitget.Net.Enums.BitgetProductTypeV2;

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
            _logger.LogInformation("Fetching all Bitget account balances...");

            // 1️⃣ Spot Account
            _logger.LogInformation("Fetching Bitget Spot balances...");
            var spotBalances = await GetBalancesAsync(cancellationToken);
            result.Spot = spotBalances
                .Where(b => b.Total > 0)
                .Select(b => new AssetBalance
                {
                    Asset = b.Asset,
                    Free = b.Available,
                    Locked = b.Locked,
                    UsdValue = 0
                }).ToList();
            _logger.LogInformation("Bitget Spot: {Count} assets", result.Spot.Count);

            // 2️⃣ USDT-M Futures
            _logger.LogInformation("Fetching Bitget USDT-M Futures balances...");
            result.UsdtMFutures = await GetUsdtMFuturesBalancesAsync(cancellationToken);
            _logger.LogInformation("Bitget USDT-M Futures: {Count} assets", result.UsdtMFutures.Count);

            // 3️⃣ Earn Account
            _logger.LogInformation("Fetching Bitget Earn account assets...");
            result.Earn = await GetEarnBalancesAsync(cancellationToken);
            _logger.LogInformation("Bitget Earn: {Count} assets", result.Earn.Count);

            // 4️⃣ Futures Bot (Copy Trading)
            _logger.LogInformation("Fetching Bitget Futures Bot (Copy Trading) balances...");
            var futuresBotBalances = await GetBotBalancesAsync(cancellationToken);
            result.Bot.AddRange(futuresBotBalances);
            _logger.LogInformation("Bitget Futures Bot: {Count} assets", futuresBotBalances.Count);

            _logger.LogInformation(
                "Bitget total accounts fetched - Spot: {Spot}, Futures: {Futures}, Earn: {Earn}, Bot: {Bot}",
                result.Spot.Count,
                result.UsdtMFutures.Count,
                result.Earn.Count,
                result.Bot.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all account balances from {Exchange}", ExchangeName);
        }

        return result;
    }

    private async Task<List<AssetBalance>> GetUsdtMFuturesBalancesAsync(CancellationToken cancellationToken = default)
    {
        var balances = new List<AssetBalance>();
        try
        {
            var result = await _client.FuturesApiV2.Account.GetBalancesAsync(BitgetProductTypeV2.UsdtFutures, cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to fetch USDT-M Futures balances from Bitget: {Error}", result.Error?.Message);
                return balances;
            }

            foreach (var account in result.Data)
            {
                if (account.Equity == 0) continue;
                balances.Add(new AssetBalance
                {
                    Asset = account.MarginAsset,
                    Free = account.Available,
                    Locked = account.Locked,
                    UsdValue = 0
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch USDT-M Futures balances from Bitget");
        }
        return balances;
    }

    private async Task<List<AssetBalance>> GetEarnBalancesAsync(CancellationToken cancellationToken = default)
    {
        var balances = new List<AssetBalance>();
        try
        {
            var endpoint = "/api/v2/earn/account/assets";
            var request = CreateAuthenticatedRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Bitget Earn account assets failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return balances;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<BitgetEarnResponse>(json);

            if (data?.Code == "00000" && data?.Data != null)
            {
                foreach (var asset in data.Data)
                {
                    if (string.IsNullOrEmpty(asset.Amount) || !decimal.TryParse(asset.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var total) || total == 0)
                        continue;

                    balances.Add(new AssetBalance
                    {
                        Asset = asset.Coin ?? string.Empty,
                        Free = total,
                        Locked = 0,
                        UsdValue = 0
                    });
                }

                _logger.LogInformation("Bitget Earn account assets: {Count} coins fetched", balances.Count);
            }
            else
            {
                _logger.LogWarning("Bitget Earn account assets returned code: {Code}, msg: {Msg}", data?.Code, data?.Msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Earn balances from Bitget");
        }
        return balances;
    }

    private async Task<List<AssetBalance>> GetBotBalancesAsync(CancellationToken cancellationToken = default)
    {
        var balances = new List<AssetBalance>();
        try
        {
            var endpoint = "/api/v2/copy/mix-trader/account-detail?productType=USDT-FUTURES";
            var request = CreateAuthenticatedRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // Ha nincs bot account, a Bitget hibakódot ad vissza a JSON-ban
            if (!response.IsSuccessStatusCode || json.Contains("\"40007\"") || json.Contains("40007"))
            {
                _logger.LogInformation("No Bot account found on Bitget (or not a copy trader)");
                return balances;
            }

            var data = JsonSerializer.Deserialize<BitgetBotResponse>(json);

            if (data?.Data != null && !string.IsNullOrEmpty(data.Data.Equity))
            {
                if (decimal.TryParse(data.Data.Equity, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var total) && total > 0)
                {
                    _ = decimal.TryParse(data.Data.Available, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var available);
                    _ = decimal.TryParse(data.Data.Locked, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var locked);
                    balances.Add(new AssetBalance
                    {
                        Asset = data.Data.MarginCoin ?? string.Empty,
                        Free = available,
                        Locked = locked,
                        UsdValue = 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Bot balances from Bitget");
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

    private class BitgetEarnResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonPropertyName("data")]
        public List<EarnAsset>? Data { get; set; }
    }

    private class EarnAsset
    {
        [JsonPropertyName("coin")]
        public string? Coin { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }
    }

    private class BitgetBotResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("data")]
        public BotAccount? Data { get; set; }
    }

    private class BotAccount
    {
        [JsonPropertyName("marginCoin")]
        public string? MarginCoin { get; set; }

        [JsonPropertyName("locked")]
        public string? Locked { get; set; }

        [JsonPropertyName("available")]
        public string? Available { get; set; }

        [JsonPropertyName("equity")]
        public string? Equity { get; set; }
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
