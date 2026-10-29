using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Notifications;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Infrastructure.Delivery;

public enum OutcomeKind
{
    /// <summary>Delivered or suppressed. Acknowledge.</summary>
    Done,

    /// <summary>Already handled earlier. Acknowledge and do nothing.</summary>
    Duplicate,

    /// <summary>Another worker is in the middle of it. Look again after a delay.</summary>
    Busy,

    /// <summary>The attempt failed and another follows after <see cref="Outcome.RetryAfter"/>.</summary>
    Retry,

    /// <summary>Out of attempts, or nothing that can ever be delivered. Dead-letter it.</summary>
    Dead,
}

public sealed record Outcome(OutcomeKind Kind, TimeSpan? RetryAfter = null, string? Reason = null);

/// <summary>
/// Handles one delivery message in three short steps, so no database transaction stays open while an SMTP
/// server is being slow:
/// <list type="number">
/// <item>claim: skip duplicates, honour preferences, move the notification to <c>Sending</c>;</item>
/// <item>deliver on the channel, outside any transaction;</item>
/// <item>record the result and, when it is final, the message id.</item>
/// </list>
/// Both database steps lock the notification row, so a relay or another worker never sees half a change.
/// </summary>
public sealed partial class NotificationProcessor(
    NotificationsDbContext db,
    IEnumerable<INotificationChannel> channels,
    IOptions<DeliveryOptions> options,
    TimeProvider clock,
    ILogger<NotificationProcessor> logger)
{
    public async Task<Outcome> ProcessAsync(Guid messageId, Guid notificationId, CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(messageId, notificationId, cancellationToken);
        if (claimed.Outcome is { } early)
        {
            return early;
        }

        var notification = claimed.Notification!;
        string? error = null;
        try
        {
            var channel = channels.Single(c => c.Channel == notification.Channel);
            await channel.DeliverAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }

        // Once something may have been sent, the result is recorded even during shutdown.
        return await RecordAsync(messageId, notificationId, error, CancellationToken.None);
    }

    private async Task<(Outcome? Outcome, Notification? Notification)> ClaimAsync(Guid messageId, Guid notificationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.ProcessedMessages.AnyAsync(m => m.MessageId == messageId, ct))
        {
            LogDuplicate(logger, messageId, notificationId);
            return (new Outcome(OutcomeKind.Duplicate), null);
        }

        var notification = await LockAsync(notificationId, ct);
        if (notification is null)
        {
            return (new Outcome(OutcomeKind.Dead, Reason: "The notification does not exist."), null);
        }

        var now = clock.GetUtcNow();
        if (notification.Status is NotificationStatus.Delivered or NotificationStatus.Suppressed or NotificationStatus.DeadLettered)
        {
            MarkProcessed(messageId, notificationId, now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            LogDuplicate(logger, messageId, notificationId);
            return (new Outcome(OutcomeKind.Duplicate), null);
        }

        if (notification.Status == NotificationStatus.Sending && notification.UpdatedAt > now - options.Value.SendingLease)
        {
            return (new Outcome(OutcomeKind.Busy, options.Value.EffectiveRetryDelays[0]), null);
        }

        var enabled = await db.ChannelPreferences.AsNoTracking()
            .Where(p => p.ClientId == notification.ClientId && p.RecipientId == notification.RecipientId && p.Channel == notification.Channel)
            .Select(p => (bool?)p.Enabled)
            .SingleOrDefaultAsync(ct);
        if (enabled == false)
        {
            notification.Suppress(now);
            MarkProcessed(messageId, notificationId, now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            LogSuppressed(logger, notificationId, notification.Channel);
            return (new Outcome(OutcomeKind.Done), null);
        }

        notification.StartAttempt(now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (null, notification);
    }

    private async Task<Outcome> RecordAsync(Guid messageId, Guid notificationId, string? error, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var notification = await LockAsync(notificationId, ct);
        if (notification is not { Status: NotificationStatus.Sending })
        {
            // Another worker took over after the lease ran out and has finished it.
            return new Outcome(OutcomeKind.Duplicate);
        }

        var now = clock.GetUtcNow();
        Outcome outcome;
        if (error is null)
        {
            notification.MarkDelivered(now);
            MarkProcessed(messageId, notificationId, now);
            outcome = new Outcome(OutcomeKind.Done);
            LogDelivered(logger, notificationId, notification.Channel, notification.Attempts);
        }
        else if (notification.Attempts < options.Value.MaxAttempts)
        {
            notification.MarkFailed(error, willRetry: true, now);
            outcome = new Outcome(OutcomeKind.Retry, options.Value.EffectiveRetryDelays[notification.Attempts - 1], error);
            LogFailed(logger, notificationId, notification.Attempts, options.Value.MaxAttempts, error);
        }
        else
        {
            notification.MarkFailed(error, willRetry: false, now);
            MarkProcessed(messageId, notificationId, now);
            outcome = new Outcome(OutcomeKind.Dead, Reason: error);
            LogDeadLettered(logger, notificationId, notification.Attempts, error);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return outcome;
    }

    /// <summary><c>xmin</c> is a system column, so it has to be named for the row version to be read.</summary>
    private Task<Notification?> LockAsync(Guid notificationId, CancellationToken ct) =>
        db.Notifications.FromSql($"SELECT *, xmin FROM notifications WHERE id = {notificationId} FOR UPDATE").SingleOrDefaultAsync(ct);

    private void MarkProcessed(Guid messageId, Guid notificationId, DateTimeOffset now) =>
        db.ProcessedMessages.Add(new ProcessedMessage { MessageId = messageId, NotificationId = notificationId, ProcessedAt = now });

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} delivered by {Channel} on attempt {Attempt}")]
    private static partial void LogDelivered(ILogger logger, Guid notificationId, Channel channel, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} suppressed: the recipient switched {Channel} off")]
    private static partial void LogSuppressed(ILogger logger, Guid notificationId, Channel channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId} attempt {Attempt} of {MaxAttempts} failed: {Error}")]
    private static partial void LogFailed(ILogger logger, Guid notificationId, int attempt, int maxAttempts, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification {NotificationId} dead-lettered after {Attempts} attempts: {Error}")]
    private static partial void LogDeadLettered(ILogger logger, Guid notificationId, int attempts, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {MessageId} for notification {NotificationId} was already handled")]
    private static partial void LogDuplicate(ILogger logger, Guid messageId, Guid notificationId);
}
