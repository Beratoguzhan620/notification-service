using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Clients;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Demo;

/// <summary>What the public demo's page needs: a sender key anyone may use, and where the sent emails can be read.</summary>
public sealed record DemoInfo(string SenderApiKey, string Mailbox);

/// <summary>
/// The public demo publishes a sender key (<c>Demo:SenderApiKey</c>) so visitors can send notifications without
/// asking anyone. Emails go only to the demo's own Mailpit, so the key reaches no real inbox. Off everywhere else.
/// </summary>
internal static partial class DemoEndpoints
{
    public const string ClientName = "Public demo";

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/demo", Info)
            .WithTags("Clients")
            .WithSummary("The public demo's sender key")
            .WithDescription("Null unless this installation is the public demo.");
        return app;
    }

    // Null rather than 404 elsewhere, so a page asking leaves no failed request in the browser console.
    private static Ok<DemoInfo?> Info(IConfiguration configuration) =>
        TypedResults.Ok<DemoInfo?>(configuration["Demo:SenderApiKey"] is { Length: > 0 } key ? new DemoInfo(key, "/mail/") : null);

    /// <summary>Creates the sender client for the published key, once.</summary>
    public static async Task SeedAsync(IServiceProvider services, string? key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var prefix = ApiKey.PrefixOf(key)
            ?? throw new InvalidOperationException($"Demo:SenderApiKey must look like '{ApiKey.Scheme}<8 lowercase letters or digits>_<at least {ApiKey.MinSecretLength} characters>'.");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        if (await db.ApiClients.AnyAsync(c => c.KeyPrefix == prefix, cancellationToken))
        {
            return;
        }

        db.ApiClients.Add(ApiClient.WithKey(ClientName, ClientRole.Sender, key, scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        LogCreated(scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoEndpoints)), prefix);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the public demo's sender client (key prefix {KeyPrefix})")]
    private static partial void LogCreated(ILogger logger, string keyPrefix);
}
