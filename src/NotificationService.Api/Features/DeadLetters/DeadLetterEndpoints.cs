using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Features.Auth;
using NotificationService.Api.Features.Notifications;
using NotificationService.Core;
using NotificationService.Core.Notifications;
using NotificationService.Core.Outbox;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.DeadLetters;

/// <summary>
/// Notifications that used up every attempt. The database is the list to work from: it keeps the rendered
/// message and the last error, while the dead queue in RabbitMQ only keeps raw messages for a while.
/// </summary>
internal static partial class DeadLetterEndpoints
{
    public static IEndpointRouteBuilder MapDeadLetterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dead-letters").WithTags("Dead letters").RequireAuthorization(ApiKeyAuthentication.AdminPolicy);

        group.MapGet("/", List).WithSummary("Dead-lettered notifications, newest first (admin)");
        group.MapPost("/{id:guid}/replay", Replay)
            .WithSummary("Send a dead letter again (admin)")
            .WithDescription("Starts over with a fresh set of attempts, through the outbox like a new request.");

        return app;
    }

    private static async Task<Ok<Page<NotificationResponse>>> List(NotificationsDbContext db, CancellationToken ct, Guid? cursor = null, int? limit = null)
    {
        var take = Paging.Clamp(limit);
        var query = db.Notifications.AsNoTracking().Where(n => n.Status == NotificationStatus.DeadLettered);
        if (cursor is { } after)
        {
            query = query.Where(n => n.Id < after);
        }

        var rows = await query.OrderByDescending(n => n.Id).Take(take + 1).ToListAsync(ct);
        return TypedResults.Ok(Paging.Create(rows, take, n => n.Id, n => NotificationResponse.Create(n, withHistory: false)));
    }

    private static async Task<Accepted<NotificationResponse>> Replay(
        Guid id, NotificationsDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var notification = await db.Notifications.Include(n => n.Events).SingleOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no notification with this id.");

        var now = clock.GetUtcNow();
        notification.Replay(now);
        db.OutboxMessages.Add(OutboxMessage.Create(
            DeliverNotification.Type,
            JsonSerializer.Serialize(new DeliverNotification(notification.Id)),
            Activity.Current?.Id,
            now));
        // The row version stops a replay from overwriting a worker's change made in the meantime.
        await db.SaveChangesAsync(ct);

        LogReplayed(loggers.CreateLogger(typeof(DeadLetterEndpoints)), notification.Id);
        return TypedResults.Accepted($"/api/notifications/{notification.Id}", NotificationResponse.Create(notification, withHistory: true));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} replayed from the dead letters")]
    private static partial void LogReplayed(ILogger logger, Guid notificationId);
}
