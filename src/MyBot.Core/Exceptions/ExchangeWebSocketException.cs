namespace MyBot.Core.Exceptions;

/// <summary>Exception thrown for WebSocket connection or subscription failures.</summary>
public class ExchangeWebSocketException : ExchangeException
{
    /// <summary>The WebSocket stream or topic that caused the error, if applicable.</summary>
    public string? StreamName { get; }

    public ExchangeWebSocketException(string exchangeName, string message, string? streamName = null, string? errorCode = null, Exception? innerException = null)
        : base(exchangeName, message, errorCode, innerException)
    {
        StreamName = streamName;
    }
}

/// <summary>Exception thrown when a WebSocket connection cannot be established or is unexpectedly closed.</summary>
public class ExchangeWebSocketConnectionException : ExchangeWebSocketException
{
    public ExchangeWebSocketConnectionException(string exchangeName, string message, Exception? innerException = null)
        : base(exchangeName, message, null, "WS_CONNECTION_FAILED", innerException) { }
}

/// <summary>Exception thrown when subscribing to a WebSocket stream fails.</summary>
public class ExchangeWebSocketSubscriptionException : ExchangeWebSocketException
{
    public ExchangeWebSocketSubscriptionException(string exchangeName, string streamName, string message, Exception? innerException = null)
        : base(exchangeName, message, streamName, "WS_SUBSCRIPTION_FAILED", innerException) { }
}
