using NotificationService.Core.Notifications;

namespace NotificationService.Infrastructure.Delivery;

/// <summary>Delivers a rendered notification on its channel. Throwing means the attempt failed.</summary>
public interface INotificationChannel
{
    Channel Channel { get; }

    Task DeliverAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed class EmailChannel(IEmailSender sender) : INotificationChannel
{
    public Channel Channel => Channel.Email;

    public Task DeliverAsync(Notification notification, CancellationToken cancellationToken) =>
        sender.SendAsync(
            new EmailMessage(
                notification.Id,
                notification.RecipientEmail ?? throw new InvalidOperationException("An email notification has no address."),
                notification.Subject,
                notification.Text,
                notification.Html),
            cancellationToken);
}

/// <summary>The inbox is the notification itself: once it is marked delivered, the recipient's app shows it.</summary>
public sealed class InAppChannel : INotificationChannel
{
    public Channel Channel => Channel.InApp;

    public Task DeliverAsync(Notification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
