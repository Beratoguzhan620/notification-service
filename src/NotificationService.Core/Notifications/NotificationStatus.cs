namespace NotificationService.Core.Notifications;

public enum NotificationStatus
{
    /// <summary>Saved together with its outbox message; not yet handed to RabbitMQ.</summary>
    Accepted,

    /// <summary>Published to the delivery queue.</summary>
    Queued,

    /// <summary>A worker is delivering it.</summary>
    Sending,

    Delivered,

    /// <summary>The last attempt failed; another one is scheduled.</summary>
    Failed,

    /// <summary>The recipient switched this channel off, so nothing was sent.</summary>
    Suppressed,

    /// <summary>Every attempt failed. An admin can replay it.</summary>
    DeadLettered,
}
