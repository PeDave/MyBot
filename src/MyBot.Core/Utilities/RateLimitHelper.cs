namespace MyBot.Core.Utilities;

/// <summary>Provides exponential backoff retry logic for handling rate-limit (HTTP 429) errors.</summary>
public static class RateLimitHelper
{
    /// <summary>
    /// Executes <paramref name="action"/> with exponential backoff retries when a 429 Too Many Requests
    /// response is detected. Delays follow 1 s → 2 s → 4 s before each successive retry.
    /// </summary>
    /// <param name="action">The async operation to execute.</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var delaySeconds = 1;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxRetries && Is429Error(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                delaySeconds *= 2;
            }
        }
        // Final attempt — let any exception propagate
        return await action();
    }

    /// <summary>
    /// Executes <paramref name="action"/> with exponential backoff retries when a 429 Too Many Requests
    /// response is detected. Delays follow 1 s → 2 s → 4 s before each successive retry.
    /// </summary>
    public static async Task ExecuteWithRetryAsync(
        Func<Task> action,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var delaySeconds = 1;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && Is429Error(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                delaySeconds *= 2;
            }
        }
        await action();
    }

    /// <summary>
    /// Wraps an <see cref="HttpResponseMessage"/> check: if the response is 429, waits with exponential
    /// backoff and re-executes <paramref name="action"/>; otherwise returns the response immediately.
    /// </summary>
    public static async Task<HttpResponseMessage> ExecuteHttpWithRetryAsync(
        Func<Task<HttpResponseMessage>> action,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var delaySeconds = 1;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var response = await action();
            if ((int)response.StatusCode != 429 || attempt >= maxRetries)
                return response;

            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            delaySeconds *= 2;
        }
        return await action();
    }

    private static bool Is429Error(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("429") || msg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }
}
