using System.Collections.Concurrent;

namespace MyBot.WebDashboard.Services.Automation;

public sealed record AutomationEvent(
    string Source,
    string EventType,
    string Payload,
    DateTimeOffset ReceivedAtUtc);

public sealed class AutomationService
{
    private readonly ConcurrentQueue<AutomationEvent> _events = new();

    public AutomationEvent Ingest(string source, string eventType, string payload)
    {
        var evt = new AutomationEvent(source, eventType, payload, DateTimeOffset.UtcNow);
        _events.Enqueue(evt);
        return evt;
    }

    public IReadOnlyList<AutomationEvent> GetRecent(int limit = 100)
        => _events.Reverse().Take(limit).ToList();
}
