using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Features.Templates;
using NotificationService.Core;
using NotificationService.Core.Notifications;
using NotificationService.Core.Outbox;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Persistence.Configurations;

namespace NotificationService.Api.Features.Notifications;

public sealed record SendNotificationRequest(
    RecipientDto Recipient,
    Channel Channel,
    string Template,
    string? Language = null,
    Dictionary<string, string>? Variables = null);

/// <summary>
/// Accepts a notification: renders the template, then saves the notification, its first history entry and
/// the outbox message in one transaction. Nothing talks to RabbitMQ here.
/// </summary>
internal sealed partial class NotificationRequests(NotificationsDbContext db, TimeProvider clock, ILogger<NotificationRequests> logger)
{
    public async Task<(Notification Notification, bool Replayed)> SendAsync(
        Guid clientId, string idempotencyKey, SendNotificationRequest request, CancellationToken ct)
    {
        var hash = RequestHash.Compute(request);
        if (await FindAsync(clientId, idempotencyKey, ct) is { } earlier)
        {
            return (SameRequestOrThrow(earlier, hash), true);
        }

        var template = await TemplateEndpoints.FindAsync(db, request.Template, ct, tracking: false);
        var version = template.Resolve(request.Language);
        var message = version.Render(request.Variables ?? []);
        var now = clock.GetUtcNow();

        var notification = Notification.Accept(
            clientId, idempotencyKey, hash, new Recipient(request.Recipient.Id, request.Recipient.Email), request.Channel,
            template, version, message, now);
        var outbox = OutboxMessage.Create(
            DeliverNotification.Type,
            JsonSerializer.Serialize(new DeliverNotification(notification.Id)),
            Activity.Current?.Id,
            now);

        db.Notifications.Add(notification);
        db.OutboxMessages.Add(outbox);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(NotificationConfiguration.IdempotencyIndex))
        {
            // The same key arrived twice at the same moment and the other request won. Answer as it did.
            db.ChangeTracker.Clear();
            var winner = await FindAsync(clientId, idempotencyKey, ct)
                ?? throw new InvalidOperationException("The notification that holds the idempotency key has disappeared.");
            return (SameRequestOrThrow(winner, hash), true);
        }

        LogAccepted(logger, notification.Id, notification.Channel, notification.TemplateKey, notification.TemplateVersion);
        return (notification, false);
    }

    private Task<Notification?> FindAsync(Guid clientId, string idempotencyKey, CancellationToken ct) =>
        db.Notifications.AsNoTracking().Include(n => n.Events)
            .SingleOrDefaultAsync(n => n.ClientId == clientId && n.IdempotencyKey == idempotencyKey, ct);

    private static Notification SameRequestOrThrow(Notification earlier, byte[] hash) =>
        earlier.RequestHash.AsSpan().SequenceEqual(hash)
            ? earlier
            : throw new DomainException(ErrorCodes.IdempotencyKeyReused,
                "This idempotency key was already used for a different request. Use a new key for a new notification.")
            {
                Details = { ["notificationId"] = earlier.Id },
            };

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} accepted: {Channel}, {TemplateKey} v{TemplateVersion}")]
    private static partial void LogAccepted(ILogger logger, Guid notificationId, Channel channel, string templateKey, int templateVersion);
}
