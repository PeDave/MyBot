namespace MyBot.Core.Exceptions;

/// <summary>Exception thrown for WebSocket-specific connection or subscription failures.</summary>
public class ExchangeWebSocketException : ExchangeException
{
    /// <summary>The stream or topic that caused the failure, if applicable.</summary>
    public string? Stream { get; }

    public ExchangeWebSocketException(string exchangeName, string message, string? stream = null, string? errorCode = null, Exception? innerException = null)
        : base(exchangeName, message, errorCode, innerException)
    {
        Stream = stream;
    }
}

/// <summary>Exception thrown when a WebSocket connection attempt fails.</summary>
public class ExchangeWebSocketConnectionException : ExchangeWebSocketException
{
    public ExchangeWebSocketConnectionException(string exchangeName, string message, Exception? innerException = null)
        : base(exchangeName, message, null, "WS_CONNECTION_FAILED", innerException) { }
}

/// <summary>Exception thrown when a WebSocket subscription attempt fails.</summary>
public class ExchangeWebSocketSubscriptionException : ExchangeWebSocketException
{
    public ExchangeWebSocketSubscriptionException(string exchangeName, string stream, string message, Exception? innerException = null)
        : base(exchangeName, message, stream, "WS_SUBSCRIPTION_FAILED", innerException) { }
}
