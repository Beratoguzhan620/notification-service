namespace NotificationService.Core.Outbox;

/// <summary>The one message on the delivery queue: "deliver this notification".</summary>
public sealed record DeliverNotification(Guid NotificationId)
{
    public const string Type = "deliver-notification";
}
