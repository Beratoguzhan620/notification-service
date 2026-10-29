using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Common;
using NotificationService.Api.Features.Auth;
using NotificationService.Core;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Notifications;

internal static class NotificationEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    private const int MaxVariables = 50;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapPost("/", Send)
            .WithSummary("Send a notification")
            .WithDescription("""
                Renders the newest version of the template in the requested language (or the template's default
                language) and queues it for delivery. Send an `Idempotency-Key` header: repeating a request with the
                same key and body returns the same notification with `Idempotent-Replayed: true`; the same key with
                a different body is refused with `idempotency_key_reused`.
                """);

        group.MapGet("/{id:guid}", Get)
            .WithSummary("A notification with its history")
            .WithDescription("Senders see their own notifications; admins see all.");

        return app;
    }

    private static async Task<Results<Accepted<NotificationResponse>, ValidationProblem>> Send(
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        SendNotificationRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        NotificationRequests requests,
        CancellationToken ct)
    {
        var validation = Validate(idempotencyKey, request);
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (notification, replayed) = await requests.SendAsync(user.ClientId(), idempotencyKey!, request, ct);
        if (replayed)
        {
            context.Response.Headers[ReplayedHeader] = "true";
        }

        return TypedResults.Accepted($"/api/notifications/{notification.Id}", NotificationResponse.Create(notification, withHistory: true));
    }

    private static async Task<Ok<NotificationResponse>> Get(Guid id, ClaimsPrincipal user, NotificationsDbContext db, CancellationToken ct)
    {
        var notification = await db.Notifications.AsNoTracking().Include(n => n.Events).SingleOrDefaultAsync(n => n.Id == id, ct);
        if (notification is null || (notification.ClientId != user.ClientId() && !user.IsInRole(ApiKeyAuthentication.AdminRole)))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no notification with this id.");
        }

        return TypedResults.Ok(NotificationResponse.Create(notification, withHistory: true));
    }

    private static Validation Validate(string? idempotencyKey, SendNotificationRequest request)
    {
        var validation = new Validation()
            .Require(idempotencyKey is { Length: > 0 and <= Notification.MaxIdempotencyKeyLength } key && key.All(c => c is > ' ' and < '\u007f'),
                IdempotencyHeader, $"Send an {IdempotencyHeader} header of 1 to {Notification.MaxIdempotencyKeyLength} printable characters, such as a UUID.")
            .Require(request.Recipient is not null, "recipient", "Say who the notification is for.")
            .Require(Enum.IsDefined(request.Channel), "channel", "The channel is Email or InApp.")
            .Require(!string.IsNullOrWhiteSpace(request.Template), "template", "Name the template, like 'order-shipped'.")
            .Require(request.Language is null || Languages.Normalize(request.Language) is not null, "language", "The language is tr or en.")
            .Require(request.Variables is null || request.Variables.Count <= MaxVariables, "variables", $"At most {MaxVariables} variables.")
            .Require(request.Variables is null || request.Variables.Values.All(v => v is not null), "variables", "Variable values are strings.");

        if (request.Recipient is { } recipient)
        {
            validation
                .Require(Recipient.IsValidId(recipient.Id), "recipient.id",
                    $"A recipient id has 1 to {Recipient.MaxIdLength} printable characters without spaces.")
                .Require(request.Channel != Channel.Email || recipient.Email is not null, "recipient.email", "Email notifications need an email address.")
                .Require(recipient.Email is null || Recipient.IsValidEmail(recipient.Email), "recipient.email", "The email address is not valid.");
        }

        return validation;
    }
}
