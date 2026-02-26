using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BingX.Net.Clients;
using CryptoExchange.Net.Authentication;
using Microsoft.Extensions.Logging;
using MyBot.Core.Exceptions;
using MyBot.Core.Interfaces;
using MyBot.Core.Models;
using BingXOrderSide = BingX.Net.Enums.OrderSide;
using BingXOrderType = BingX.Net.Enums.OrderType;
using BingXOrderStatus = BingX.Net.Enums.OrderStatus;
using BingXTimeInForce = BingX.Net.Enums.TimeInForce;
using BingXOrderDetails = BingX.Net.Objects.Models.BingXOrderDetails;
using BingXKlineInterval = BingX.Net.Enums.KlineInterval;

namespace MyBot.Exchanges.BingX;

/// <summary>Wrapper for the BingX cryptocurrency exchange using JK.BingX.Net SDK.</summary>
public class BingXWrapper : IExchangeWrapper, IDisposable
{
    private readonly BingXRestClient _client;
    private readonly ILogger<BingXWrapper> _logger;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly HttpClient _httpClient;
    private const string _baseUrl = "https://open-api.bingx.com";

    public string ExchangeName => "BingX";

    public BingXWrapper(string apiKey, string apiSecret, ILogger<BingXWrapper> logger)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _logger = logger;
        _httpClient = new HttpClient();
        _client = new BingXRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
        });
    }

    public async Task<IEnumerable<UnifiedBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SpotApi.Account.GetBalancesAsync(ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get balances", result.Error?.Code?.ToString());

            return result.Data.Select(b => new UnifiedBalance
            {
                Asset = b.Asset,
                Available = b.Free,
                Locked = b.Locked,
                Total = b.Total,
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
            _logger.LogInformation("BingX Spot: {Count} assets", result.Spot.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting spot balances from {Exchange}", ExchangeName);
        }
        try
        {
            result.Wealth = await GetWealthBalancesAsync(cancellationToken);
            _logger.LogInformation("BingX Wealth: {Count} assets", result.Wealth.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting wealth balances from {Exchange}", ExchangeName);
        }
        return result;
    }

    private async Task<List<AssetBalance>> GetWealthBalancesAsync(CancellationToken cancellationToken = default)
    {
        var balances = new List<AssetBalance>();
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var queryString = $"timestamp={timestamp}";
            var signature = GenerateSignature(queryString);
            var url = $"{_baseUrl}/openApi/wallets/v1/capital/config/getall?{queryString}&signature={signature}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-BX-APIKEY", _apiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("BingX wallet balances failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return balances;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("BingX wallet raw response: {Json}", json);
            var data = JsonSerializer.Deserialize<BingXWalletResponse>(json);

            if (data?.Code == 0 && data.Data != null)
            {
                foreach (var item in data.Data)
                {
                    var free = decimal.TryParse(item.Free, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0;
                    var locked = decimal.TryParse(item.Locked, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : 0;
                    if (free + locked == 0) continue;
                    if (string.IsNullOrEmpty(item.Coin))
                    {
                        _logger.LogWarning("BingX wallet returned an asset with null/empty coin, skipping");
                        continue;
                    }
                    balances.Add(new AssetBalance
                    {
                        Asset = item.Coin,
                        Free = free,
                        Locked = locked,
                        UsdValue = 0
                    });
                    _logger.LogInformation("BingX wallet - {Coin}: free={Free}, locked={Locked}", item.Coin, free, locked);
                }
                _logger.LogInformation("BingX wallet: {Count} assets fetched", balances.Count);
            }
            else
            {
                _logger.LogWarning("BingX wallet response code: {Code}, msg: {Msg}", data?.Code, data?.Msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch wallet balances from {Exchange}", ExchangeName);
        }
        return balances;
    }

    private string GenerateSignature(string queryString)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_apiSecret);
        var messageBytes = Encoding.UTF8.GetBytes(queryString);
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private class BingXWalletResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonPropertyName("data")]
        public List<BingXWalletItem>? Data { get; set; }
    }

    private class BingXWalletItem
    {
        [JsonPropertyName("coin")]
        public string? Coin { get; set; }

        [JsonPropertyName("free")]
        public string? Free { get; set; }

        [JsonPropertyName("locked")]
        public string? Locked { get; set; }
    }

    public async Task<UnifiedOrder> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity,
        decimal? price = null, TimeInForce timeInForce = TimeInForce.GoodTillCanceled, CancellationToken cancellationToken = default)
    {
        try
        {
            var bxSide = side == OrderSide.Buy ? BingXOrderSide.Buy : BingXOrderSide.Sell;
            var bxType = type == OrderType.Market ? BingXOrderType.Market : BingXOrderType.Limit;
            var bxTif = MapTimeInForce(timeInForce);

            var result = await _client.SpotApi.Trading.PlaceOrderAsync(
                symbol, bxSide, bxType,
                quantity: quantity,
                price: price,
                timeInForce: bxTif,
                ct: cancellationToken);

            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to place order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = symbol,
                Side = side,
                Type = type,
                Quantity = quantity,
                Price = price,
                Status = MapOrderStatus(result.Data.Status),
                TimeInForce = timeInForce,
                CreateTime = result.Data.Timestamp,
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
            var longOrderId = long.TryParse(orderId, out var parsedCancel) ? (long?)parsedCancel
                : throw new ExchangeException(ExchangeName, $"Invalid orderId format for BingX: '{orderId}' (must be a numeric ID)");
            var result = await _client.SpotApi.Trading.CancelOrderAsync(symbol, longOrderId, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to cancel order", result.Error?.Code?.ToString());

            return new UnifiedOrder
            {
                OrderId = result.Data.OrderId.ToString(),
                ClientOrderId = result.Data.ClientOrderId,
                Symbol = result.Data.Symbol,
                Side = result.Data.Side == BingXOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                Status = MapOrderStatus(result.Data.Status),
                Quantity = result.Data.Quantity,
                QuantityFilled = result.Data.QuantityFilled,
                Price = result.Data.Price,
                CreateTime = result.Data.Timestamp,
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
            var longOrderId = long.TryParse(orderId, out var parsedGet) ? (long?)parsedGet
                : throw new ExchangeException(ExchangeName, $"Invalid orderId format for BingX: '{orderId}' (must be a numeric ID)");
            var result = await _client.SpotApi.Trading.GetOrderAsync(symbol, longOrderId, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order", result.Error?.Code?.ToString());

            return MapOrder(result.Data);
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
            var result = await _client.SpotApi.Trading.GetOpenOrdersAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get open orders", result.Error?.Code?.ToString());

            return result.Data.Select(MapOrder);
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
            var result = await _client.SpotApi.Trading.GetOrdersAsync(symbol, pageSize: limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order history", result.Error?.Code?.ToString());

            return result.Data.Select(MapOrder);
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
            var result = await _client.SpotApi.ExchangeData.GetTickersAsync(symbol, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get ticker", result.Error?.Code?.ToString());

            var t = result.Data.FirstOrDefault()
                ?? throw new ExchangeException(ExchangeName, $"Ticker for {symbol} not found");

            decimal changePercent = 0;
            if (decimal.TryParse(t.PriceChangePercent?.TrimEnd('%'), out var pct))
                changePercent = pct;

            return new UnifiedTicker
            {
                Symbol = t.Symbol,
                LastPrice = t.LastPrice,
                BidPrice = t.BestBidPrice,
                AskPrice = t.BestAskPrice,
                High24h = t.HighPrice,
                Low24h = t.LowPrice,
                Volume24h = t.Volume,
                QuoteVolume24h = t.QuoteVolume,
                ChangePercent24h = changePercent,
                Exchange = ExchangeName,
                Timestamp = t.CloseTime
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
            var result = await _client.SpotApi.ExchangeData.GetOrderBookAsync(symbol, limit: depth, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get order book", result.Error?.Code?.ToString());

            return new UnifiedOrderBook
            {
                Symbol = result.Data.Symbol ?? symbol,
                Bids = result.Data.Bids.Select(b => new OrderBookEntry { Price = b.Price, Quantity = b.Quantity }).ToList(),
                Asks = result.Data.Asks.Select(a => new OrderBookEntry { Price = a.Price, Quantity = a.Quantity }).ToList(),
                Exchange = ExchangeName,
                Timestamp = result.Data.Timestamp ?? DateTime.UtcNow
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
            var result = await _client.SpotApi.ExchangeData.GetRecentTradesAsync(symbol, limit: limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get recent trades", result.Error?.Code?.ToString());

            return result.Data.Select(t => new UnifiedTrade
            {
                TradeId = t.Id.ToString(),
                Symbol = symbol,
                Price = t.Price,
                Quantity = t.Quantity,
                QuoteQuantity = t.Price * t.Quantity,
                TakerSide = t.BuyerIsMaker ? OrderSide.Sell : OrderSide.Buy,
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

    private UnifiedOrder MapOrder(BingXOrderDetails order) => new()
    {
        OrderId = order.OrderId.ToString(),
        ClientOrderId = order.ClientOrderId,
        Symbol = order.Symbol,
        Side = order.Side == BingXOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
        Type = MapOrderType(order.Type),
        Status = MapOrderStatus(order.Status),
        Quantity = order.Quantity,
        QuantityFilled = order.QuantityFilled,
        Price = order.Price,
        AveragePrice = order.AveragePrice,
        QuoteQuantityFilled = order.QuoteQuantity,
        CreateTime = order.CreateTime,
        UpdateTime = order.UpdateTime,
        Exchange = ExchangeName
    };

    private static OrderStatus MapOrderStatus(BingXOrderStatus status) => status switch
    {
        BingXOrderStatus.New => OrderStatus.New,
        BingXOrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        BingXOrderStatus.Filled => OrderStatus.Filled,
        BingXOrderStatus.Canceled => OrderStatus.Canceled,
        BingXOrderStatus.Failed => OrderStatus.Rejected,
        _ => OrderStatus.Unknown
    };

    private static OrderType MapOrderType(BingXOrderType type) => type switch
    {
        BingXOrderType.Market => OrderType.Market,
        _ => OrderType.Limit
    };

    private static BingXTimeInForce MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.ImmediateOrCancel => BingXTimeInForce.ImmediateOrCancel,
        TimeInForce.FillOrKill => BingXTimeInForce.FillOrKill,
        _ => BingXTimeInForce.GoodTillCanceled
    };

    public async Task<IEnumerable<UnifiedKline>> GetKlinesAsync(string symbol, string timeframe, DateTime startTime, DateTime endTime, int limit = 200, CancellationToken cancellationToken = default)
    {
        try
        {
            var interval = timeframe switch
            {
                "1m" => BingXKlineInterval.OneMinute,
                "5m" => BingXKlineInterval.FiveMinutes,
                "15m" => BingXKlineInterval.FifteenMinutes,
                "30m" => BingXKlineInterval.ThirtyMinutes,
                "1h" => BingXKlineInterval.OneHour,
                "4h" => BingXKlineInterval.FourHours,
                "1d" => BingXKlineInterval.OneDay,
                _ => throw new ArgumentException($"Unsupported timeframe: {timeframe}")
            };
            var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, interval, startTime, endTime, limit, ct: cancellationToken);
            if (!result.Success)
                throw new ExchangeException(ExchangeName, result.Error?.Message ?? "Failed to get klines", result.Error?.Code?.ToString());
            // Filter defensively to guarantee results are within the requested [startTime, endTime] range.
            return result.Data
                .Where(k => k.OpenTime >= startTime && k.OpenTime <= endTime)
                .Select(k => new UnifiedKline
                {
                    OpenTime = k.OpenTime,
                    CloseTime = k.CloseTime,
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
