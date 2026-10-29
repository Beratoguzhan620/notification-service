using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Common;
using NotificationService.Core;
using NotificationService.Core.Notifications;
using NotificationService.Core.Preferences;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Preferences;

/// <summary>Only the channels given are changed; a missing field leaves that channel as it is.</summary>
public sealed record UpdatePreferencesRequest(bool? Email = null, bool? InApp = null);

public sealed record PreferencesResponse(string RecipientId, bool Email, bool InApp, DateTimeOffset? UpdatedAt);

internal static class PreferenceEndpoints
{
    public static IEndpointRouteBuilder MapPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipients/{recipientId}/preferences").WithTags("Preferences").RequireAuthorization();

        group.MapGet("/", Get)
            .WithSummary("A recipient's channel preferences")
            .WithDescription("Recipients are your own ids. Every channel is on until switched off.");
        group.MapPatch("/", Update)
            .WithSummary("Switch channels on or off")
            .WithDescription("Notifications for a switched-off channel are recorded as `Suppressed` instead of being delivered.");

        return app;
    }

    private static async Task<Results<Ok<PreferencesResponse>, ValidationProblem>> Get(
        string recipientId, ClaimsPrincipal user, NotificationsDbContext db, CancellationToken ct)
    {
        if (!Recipient.IsValidId(recipientId))
        {
            return InvalidRecipient();
        }

        var rows = await db.ChannelPreferences.AsNoTracking()
            .Where(p => p.ClientId == user.ClientId() && p.RecipientId == recipientId)
            .ToListAsync(ct);
        return TypedResults.Ok(Response(recipientId, rows));
    }

    private static async Task<Results<Ok<PreferencesResponse>, ValidationProblem>> Update(
        string recipientId, UpdatePreferencesRequest request, ClaimsPrincipal user, NotificationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Recipient.IsValidId(recipientId))
        {
            return InvalidRecipient();
        }

        var clientId = user.ClientId();
        var now = clock.GetUtcNow();
        var rows = await db.ChannelPreferences
            .Where(p => p.ClientId == clientId && p.RecipientId == recipientId)
            .ToListAsync(ct);

        foreach (var (channel, enabled) in new[] { (Channel.Email, request.Email), (Channel.InApp, request.InApp) })
        {
            if (enabled is not { } value)
            {
                continue;
            }

            var row = rows.SingleOrDefault(p => p.Channel == channel);
            if (row is null)
            {
                row = ChannelPreference.Create(clientId, recipientId, channel, value, now);
                db.ChannelPreferences.Add(row);
                rows.Add(row);
            }
            else
            {
                row.Set(value, now);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.ConcurrentUpdate, "The preferences changed at the same moment. Read them again and retry.");
        }

        return TypedResults.Ok(Response(recipientId, rows));
    }

    internal static ValidationProblem InvalidRecipient() => new Validation()
        .Require(false, "recipientId", $"A recipient id has 1 to {Recipient.MaxIdLength} printable characters without spaces.")
        .ToProblem();

    private static PreferencesResponse Response(string recipientId, List<ChannelPreference> rows) => new(
        recipientId,
        rows.SingleOrDefault(p => p.Channel == Channel.Email)?.Enabled ?? true,
        rows.SingleOrDefault(p => p.Channel == Channel.InApp)?.Enabled ?? true,
        rows.Count == 0 ? null : rows.Max(p => p.UpdatedAt));
}
