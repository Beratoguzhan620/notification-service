namespace NotificationService.Core.Notifications;

/// <summary>One entry in a notification's history.</summary>
public sealed class NotificationEvent
{
    public const int MaxDetailLength = 500;

    private NotificationEvent()
    {
    }

    public long Id { get; private set; }

    public Guid NotificationId { get; private set; }

    public NotificationStatus Status { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string? Detail { get; private set; }

    internal static NotificationEvent Create(Guid notificationId, NotificationStatus status, DateTimeOffset now, string? detail) => new()
    {
        NotificationId = notificationId,
        Status = status,
        OccurredAt = now,
        Detail = detail is { Length: > MaxDetailLength } ? detail[..MaxDetailLength] : detail,
    };
}
