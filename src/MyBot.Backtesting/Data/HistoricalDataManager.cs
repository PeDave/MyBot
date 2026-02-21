using Microsoft.Extensions.Logging;
using MyBot.Backtesting.Models;
using MyBot.Core.Interfaces;

namespace MyBot.Backtesting.Data;

/// <summary>
/// Manages historical OHLCV candle data by fetching from exchange wrappers
/// and caching results to local CSV files.
/// </summary>
public class HistoricalDataManager : IHistoricalDataProvider
{
    private readonly IReadOnlyDictionary<string, IExchangeWrapper> _exchanges;
    private readonly ILogger<HistoricalDataManager> _logger;
    private readonly string _cacheDirectory;

    /// <summary>
    /// Initializes a new instance of <see cref="HistoricalDataManager"/>.
    /// </summary>
    /// <param name="exchanges">Dictionary mapping exchange name (lowercase) to its wrapper.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cacheDirectory">Directory to cache downloaded data. Defaults to "./data_cache".</param>
    public HistoricalDataManager(
        IReadOnlyDictionary<string, IExchangeWrapper> exchanges,
        ILogger<HistoricalDataManager> logger,
        string cacheDirectory = "./data_cache")
    {
        _exchanges = exchanges;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <inheritdoc/>
    public async Task<List<OHLCVCandle>> GetHistoricalDataAsync(
        string exchange,
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string timeframe,
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(exchange, symbol, startDate, endDate, timeframe);

        var cacheFile = GetCacheFilePath(exchange, symbol, timeframe, startDate, endDate);

        // Try cache first
        if (File.Exists(cacheFile))
        {
            _logger.LogInformation("Loading cached data from {CacheFile}", cacheFile);
            var cached = LoadFromCsv(cacheFile);
            if (cached.Count > 0)
                return cached;
        }

        // Fetch from exchange
        _logger.LogInformation("Fetching historical data for {Symbol} from {Exchange} ({Timeframe}, {Start:yyyy-MM-dd} to {End:yyyy-MM-dd})",
            symbol, exchange, timeframe, startDate, endDate);

        var exchangeKey = exchange.ToLowerInvariant();
        if (!_exchanges.TryGetValue(exchangeKey, out var wrapper))
            throw new ArgumentException($"Exchange '{exchange}' not found. Available: {string.Join(", ", _exchanges.Keys)}");

        var candles = await FetchAllCandlesAsync(wrapper, symbol, timeframe, startDate, endDate, cancellationToken);

        // Validate and sort
        candles = candles.OrderBy(c => c.Timestamp).ToList();
        ValidateDataQuality(candles, timeframe);

        // Cache results
        SaveToCsv(cacheFile, candles);
        _logger.LogInformation("Fetched and cached {Count} candles", candles.Count);

        return candles;
    }

    private async Task<List<OHLCVCandle>> FetchAllCandlesAsync(
        IExchangeWrapper wrapper,
        string symbol,
        string timeframe,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var allCandles = new List<OHLCVCandle>();
        var batchSize = 200;
        var current = startDate;
        var intervalMinutes = TimeframeToMinutes(timeframe);

        while (current < endDate)
        {
            var batchEnd = current.AddMinutes(intervalMinutes * batchSize);
            if (batchEnd > endDate) batchEnd = endDate;

            var klines = await wrapper.GetKlinesAsync(symbol, timeframe, current, batchEnd, batchSize, cancellationToken);
            var batch = klines.Select(k => new OHLCVCandle
            {
                Timestamp = k.OpenTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume,
                Symbol = symbol,
                Exchange = wrapper.ExchangeName
            }).ToList();

            if (batch.Count == 0) break;

            allCandles.AddRange(batch);
            current = batch.Max(c => c.Timestamp).AddMinutes(intervalMinutes);

            // Small delay to respect rate limits
            if (current < endDate)
                await Task.Delay(100, cancellationToken);
        }

        return allCandles;
    }

    private static int TimeframeToMinutes(string timeframe) => timeframe switch
    {
        "1m" => 1,
        "5m" => 5,
        "15m" => 15,
        "30m" => 30,
        "1h" => 60,
        "4h" => 240,
        "1d" => 1440,
        _ => throw new ArgumentException($"Unsupported timeframe: {timeframe}")
    };

    private string GetCacheFilePath(string exchange, string symbol, string timeframe, DateTime startDate, DateTime endDate)
    {
        var fileName = $"{exchange}_{symbol}_{timeframe}_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv";
        return Path.Combine(_cacheDirectory, fileName);
    }

    private void ValidateParameters(string exchange, string symbol, DateTime startDate, DateTime endDate, string timeframe)
    {
        if (string.IsNullOrWhiteSpace(exchange))
            throw new ArgumentException("Exchange cannot be empty.", nameof(exchange));
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));
        if (startDate >= endDate)
            throw new ArgumentException("Start date must be before end date.");
        var validTimeframes = new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d" };
        if (!validTimeframes.Contains(timeframe))
            throw new ArgumentException($"Invalid timeframe '{timeframe}'. Valid: {string.Join(", ", validTimeframes)}");
    }

    private void ValidateDataQuality(List<OHLCVCandle> candles, string timeframe)
    {
        if (candles.Count == 0) return;

        var intervalMinutes = TimeframeToMinutes(timeframe);
        var expectedInterval = TimeSpan.FromMinutes(intervalMinutes);
        var tolerance = expectedInterval * 0.1; // 10% tolerance

        for (var i = 1; i < candles.Count; i++)
        {
            var gap = candles[i].Timestamp - candles[i - 1].Timestamp;
            if (gap > expectedInterval + tolerance)
                _logger.LogWarning("Data gap detected between {Time1} and {Time2} ({Gap:F0} min expected {Expected:F0} min)",
                    candles[i - 1].Timestamp, candles[i].Timestamp, gap.TotalMinutes, expectedInterval.TotalMinutes);
        }
    }

    private static void SaveToCsv(string filePath, List<OHLCVCandle> candles)
    {
        var lines = new List<string> { "Timestamp,Open,High,Low,Close,Volume,Symbol,Exchange" };
        lines.AddRange(candles.Select(c =>
            $"{c.Timestamp:O},{c.Open},{c.High},{c.Low},{c.Close},{c.Volume},{c.Symbol},{c.Exchange}"));
        File.WriteAllLines(filePath, lines);
    }

    private static List<OHLCVCandle> LoadFromCsv(string filePath)
    {
        var candles = new List<OHLCVCandle>();
        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines.Skip(1)) // skip header
        {
            var parts = line.Split(',');
            if (parts.Length < 8) continue;
            try
            {
                candles.Add(new OHLCVCandle
                {
                    Timestamp = DateTime.Parse(parts[0]),
                    Open = decimal.Parse(parts[1]),
                    High = decimal.Parse(parts[2]),
                    Low = decimal.Parse(parts[3]),
                    Close = decimal.Parse(parts[4]),
                    Volume = decimal.Parse(parts[5]),
                    Symbol = parts[6],
                    Exchange = parts[7]
                });
            }
            catch { /* skip malformed lines */ }
        }
        return candles;
    }
}
