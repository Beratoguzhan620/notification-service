using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Common;
using NotificationService.Api.Features.Notifications;
using NotificationService.Api.Features.Preferences;
using NotificationService.Core;
using NotificationService.Core.Notifications;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Inbox;

public sealed record InboxItem(Guid Id, string Template, string Subject, string Text, DateTimeOffset DeliveredAt, DateTimeOffset? ReadAt)
{
    public static InboxItem Create(Notification n) => new(n.Id, n.TemplateKey, n.Subject, n.Text, n.DeliveredAt ?? n.UpdatedAt, n.ReadAt);
}

public sealed record InboxPage(List<InboxItem> Items, Guid? NextCursor, int Unread);

public sealed record MarkedRead(int Marked);

/// <summary>What an app shows in a recipient's notification bell: delivered in-app notifications.</summary>
internal static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipients/{recipientId}/inbox").WithTags("Inbox").RequireAuthorization();

        group.MapGet("/", List)
            .WithSummary("A recipient's inbox, newest first")
            .WithDescription("Delivered in-app notifications with the number still unread. `unreadOnly=true` hides the read ones.");
        group.MapPost("/{id:guid}/read", MarkRead).WithSummary("Mark one notification as read");
        group.MapPost("/read-all", MarkAllRead).WithSummary("Mark everything as read");

        return app;
    }

    private static async Task<Results<Ok<InboxPage>, ValidationProblem>> List(
        string recipientId, ClaimsPrincipal user, NotificationsDbContext db, CancellationToken ct,
        bool unreadOnly = false, Guid? cursor = null, int? limit = null)
    {
        if (!Recipient.IsValidId(recipientId))
        {
            return PreferenceEndpoints.InvalidRecipient();
        }

        var inbox = Inbox(db, user.ClientId(), recipientId);
        var unread = await inbox.CountAsync(n => n.ReadAt == null, ct);

        var query = unreadOnly ? inbox.Where(n => n.ReadAt == null) : inbox;
        if (cursor is { } after)
        {
            query = query.Where(n => n.Id < after);
        }

        var take = Paging.Clamp(limit);
        var rows = await query.OrderByDescending(n => n.Id).Take(take + 1).ToListAsync(ct);
        var page = Paging.Create(rows, take, n => n.Id, InboxItem.Create);
        return TypedResults.Ok(new InboxPage(page.Items, page.NextCursor, unread));
    }

    private static async Task<Results<Ok<InboxItem>, ValidationProblem>> MarkRead(
        string recipientId, Guid id, ClaimsPrincipal user, NotificationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Recipient.IsValidId(recipientId))
        {
            return PreferenceEndpoints.InvalidRecipient();
        }

        var clientId = user.ClientId();
        var notification = await db.Notifications
            .SingleOrDefaultAsync(n => n.Id == id && n.ClientId == clientId && n.RecipientId == recipientId && n.Channel == Channel.InApp, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This recipient has no such notification in their inbox.");

        notification.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(InboxItem.Create(notification));
    }

    private static async Task<Results<Ok<MarkedRead>, ValidationProblem>> MarkAllRead(
        string recipientId, ClaimsPrincipal user, NotificationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Recipient.IsValidId(recipientId))
        {
            return PreferenceEndpoints.InvalidRecipient();
        }

        var now = clock.GetUtcNow();
        var marked = await Inbox(db, user.ClientId(), recipientId)
            .Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now).SetProperty(n => n.UpdatedAt, now), ct);
        return TypedResults.Ok(new MarkedRead(marked));
    }

    private static IQueryable<Notification> Inbox(NotificationsDbContext db, Guid clientId, string recipientId) =>
        db.Notifications.AsNoTracking().Where(n =>
            n.ClientId == clientId && n.RecipientId == recipientId && n.Channel == Channel.InApp && n.Status == NotificationStatus.Delivered);
}
