using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Notifications;
using NotificationService.Core.Outbox;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Telemetry;
using RabbitMQ.Client;

namespace NotificationService.Infrastructure.Outbox;

/// <summary>
/// Moves outbox rows to RabbitMQ. Each batch is one transaction: the rows are locked with
/// <c>SKIP LOCKED</c> so several workers share the work, each message is published with a confirm, and only
/// then is the row marked as published. If the process dies in between, the row is published again and the
/// consumer recognises the duplicate by its message id.
/// </summary>
public sealed partial class OutboxRelay(
    IServiceScopeFactory scopes,
    Publisher publisher,
    IOptions<DeliveryOptions> options,
    TimeProvider clock,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int published;
            try
            {
                published = await RelayBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex);
                published = 0;
            }

            if (published == 0)
            {
                await Task.Delay(options.Value.RelayInterval, clock, stoppingToken);
            }
        }
    }

    /// <summary>Publishes one batch and returns how many messages went out.</summary>
    public async Task<int> RelayBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var batch = options.Value.RelayBatchSize;
        var messages = await db.OutboxMessages
            .FromSql($"SELECT * FROM outbox_messages WHERE published_at IS NULL ORDER BY created_at LIMIT {batch} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            return 0;
        }

        // Lock the notifications too: a consumer that is quick enough to see the message before this
        // transaction commits waits for it, and then finds the notification already marked as queued.
        var ids = messages.Select(NotificationIdOf).OfType<Guid>().ToArray();
        var notifications = await db.Notifications
            .FromSql($"SELECT *, xmin FROM notifications WHERE id = ANY({ids}) FOR UPDATE")
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        var published = 0;
        foreach (var message in messages)
        {
            var now = clock.GetUtcNow();
            // The span continues the trace of the request that wrote the row, and its id travels with the message.
            using var activity = Tracing.StartLinked($"publish {Queues.Deliver}", ActivityKind.Producer, message.TraceParent);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.operation.type", "send");
            activity?.SetTag("messaging.destination.name", Queues.Deliver);
            activity?.SetTag("messaging.message.id", message.Id.ToString());
            try
            {
                await publisher.PublishAsync(Queues.Deliver, Properties(message, activity?.Id ?? message.TraceParent, now),
                    Encoding.UTF8.GetBytes(message.Payload), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The broker is unavailable or refused the message; keep the rest for the next round.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                message.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
                LogPublishFailed(logger, message.Id, ex.Message);
                break;
            }

            message.MarkPublished(now);
            if (NotificationIdOf(message) is { } id && notifications.TryGetValue(id, out var notification))
            {
                notification.MarkQueued(now);
            }

            published++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (published > 0)
        {
            LogPublished(logger, published);
        }

        return published;
    }

    private static BasicProperties Properties(OutboxMessage message, string? traceParent, DateTimeOffset now)
    {
        var headers = new Dictionary<string, object?> { [MessageHeaders.Attempt] = 1 };
        if (traceParent is not null)
        {
            headers[MessageHeaders.TraceParent] = traceParent;
        }

        return new BasicProperties
        {
            MessageId = message.Id.ToString(),
            Type = message.Type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(now.ToUnixTimeSeconds()),
            Headers = headers,
        };
    }

    private static Guid? NotificationIdOf(OutboxMessage message) =>
        message.Type == DeliverNotification.Type
            ? JsonSerializer.Deserialize<DeliverNotification>(message.Payload, JsonSerializerOptions.Web)?.NotificationId
            : null;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published {Count} outbox messages")]
    private static partial void LogPublished(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish outbox message {MessageId}: {Reason}")]
    private static partial void LogPublishFailed(ILogger logger, Guid messageId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox relay failed; it will try again")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);
}
