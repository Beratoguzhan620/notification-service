using NotificationService.Core.Templates;

namespace NotificationService.Core.Notifications;

/// <summary>
/// One message to one recipient on one channel. It holds the rendered text, so every attempt delivers exactly
/// what was accepted, and it records each status change in its history.
/// </summary>
public sealed class Notification
{
    public const int MaxIdempotencyKeyLength = 100;
    public const int MaxErrorLength = 500;

    private readonly List<NotificationEvent> _events = [];

    private Notification()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClientId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the request, to tell a retry of the same request from a different one with the same key.</summary>
    public byte[] RequestHash { get; private set; } = [];

    public string RecipientId { get; private set; } = string.Empty;

    public string? RecipientEmail { get; private set; }

    public Channel Channel { get; private set; }

    public Guid TemplateVersionId { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public int TemplateVersion { get; private set; }

    public string Language { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public string? Html { get; private set; }

    public NotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>PostgreSQL's row version, so a worker and an admin cannot overwrite each other's change.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<NotificationEvent> Events => _events;

    public static Notification Accept(
        Guid clientId, string idempotencyKey, byte[] requestHash, Recipient recipient, Channel channel,
        Template template, TemplateVersion version, RenderedMessage message, DateTimeOffset now)
    {
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(now),
            ClientId = clientId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            RecipientId = recipient.Id,
            RecipientEmail = recipient.Email,
            Channel = channel,
            TemplateVersionId = version.Id,
            TemplateKey = template.Key,
            TemplateVersion = version.Version,
            Language = version.Language,
            Subject = message.Subject,
            Text = message.Text,
            Html = channel == Channel.Email ? message.Html : null,
            Status = NotificationStatus.Accepted,
            CreatedAt = now,
            UpdatedAt = now,
        };
        notification.Record(NotificationStatus.Accepted, now, $"{template.Key} v{version.Version} ({version.Language})");
        return notification;
    }

    /// <summary>The outbox relay handed it to RabbitMQ.</summary>
    public void MarkQueued(DateTimeOffset now)
    {
        if (Status == NotificationStatus.Accepted)
        {
            Move(NotificationStatus.Queued, now, null);
        }
    }

    /// <summary>A worker starts an attempt. Returns <c>false</c> when there is nothing left to do.</summary>
    public bool StartAttempt(DateTimeOffset now)
    {
        if (Status is not (NotificationStatus.Accepted or NotificationStatus.Queued or NotificationStatus.Failed or NotificationStatus.Sending))
        {
            return false;
        }

        Attempts++;
        Move(NotificationStatus.Sending, now, $"attempt {Attempts}");
        return true;
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        EnsureStatus(NotificationStatus.Sending);
        DeliveredAt = now;
        LastError = null;
        Move(NotificationStatus.Delivered, now, null);
    }

    public void Suppress(DateTimeOffset now)
    {
        Move(NotificationStatus.Suppressed, now, $"the recipient switched {Channel} off");
    }

    /// <summary>An attempt failed: <see cref="NotificationStatus.Failed"/> when another follows, otherwise dead-lettered.</summary>
    public void MarkFailed(string error, bool willRetry, DateTimeOffset now)
    {
        EnsureStatus(NotificationStatus.Sending);
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        Move(willRetry ? NotificationStatus.Failed : NotificationStatus.DeadLettered, now, LastError);
    }

    /// <summary>A message that could not be processed at all goes straight to the dead letters.</summary>
    public void MarkDeadLettered(string reason, DateTimeOffset now)
    {
        LastError = reason.Length > MaxErrorLength ? reason[..MaxErrorLength] : reason;
        Move(NotificationStatus.DeadLettered, now, LastError);
    }

    /// <summary>An admin sends a dead letter again, with a fresh set of attempts.</summary>
    public void Replay(DateTimeOffset now)
    {
        EnsureStatus(NotificationStatus.DeadLettered);
        Attempts = 0;
        Move(NotificationStatus.Accepted, now, "replayed");
    }

    /// <summary>The recipient opened it in their inbox. Reading twice keeps the first time.</summary>
    public void MarkRead(DateTimeOffset now)
    {
        if (Channel != Channel.InApp || Status != NotificationStatus.Delivered)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only delivered in-app notifications can be marked as read.");
        }

        ReadAt ??= now;
        UpdatedAt = now;
    }

    private void EnsureStatus(NotificationStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"The notification is {Status}, not {expected}.");
        }
    }

    private void Move(NotificationStatus status, DateTimeOffset now, string? detail)
    {
        Status = status;
        UpdatedAt = now;
        Record(status, now, detail);
    }

    private void Record(NotificationStatus status, DateTimeOffset now, string? detail) =>
        _events.Add(NotificationEvent.Create(Id, status, now, detail));
}
