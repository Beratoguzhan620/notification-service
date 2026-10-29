namespace NotificationService.Core.Outbox;

/// <summary>
/// A message saved in the same transaction as the change it announces. The relay publishes it to RabbitMQ
/// afterwards, so a message is never lost and never sent for a change that was rolled back.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxErrorLength = 500;

    private OutboxMessage()
    {
    }

    /// <summary>Also the RabbitMQ message id, which the consumer uses to skip duplicates.</summary>
    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    /// <summary>The W3C trace context of the request that created it, so the trace continues in the worker.</summary>
    public string? TraceParent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string payload, string? traceParent, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Type = type,
        Payload = payload,
        TraceParent = traceParent,
        CreatedAt = now,
    };

    public void MarkPublished(DateTimeOffset now)
    {
        Attempts++;
        PublishedAt = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }
}
