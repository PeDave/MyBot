namespace MyBot.Core.Exceptions;

/// <summary>Base exception for exchange-related errors.</summary>
public class ExchangeException : Exception
{
    /// <summary>The name of the exchange where the error occurred.</summary>
    public string ExchangeName { get; }

    /// <summary>Optional exchange-specific error code.</summary>
    public string? ErrorCode { get; }

    public ExchangeException(string exchangeName, string message, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ExchangeName = exchangeName;
        ErrorCode = errorCode;
    }
}

/// <summary>Exception thrown when exchange authentication fails.</summary>
public class ExchangeAuthenticationException : ExchangeException
{
    public ExchangeAuthenticationException(string exchangeName, string message, Exception? innerException = null)
        : base(exchangeName, message, "AUTH_FAILED", innerException) { }
}

/// <summary>Exception thrown when exchange rate limits are exceeded.</summary>
public class ExchangeRateLimitException : ExchangeException
{
    /// <summary>Suggested wait time before retrying (if provided by exchange).</summary>
    public TimeSpan? RetryAfter { get; }

    public ExchangeRateLimitException(string exchangeName, string message, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(exchangeName, message, "RATE_LIMIT", innerException)
    {
        RetryAfter = retryAfter;
    }
}
