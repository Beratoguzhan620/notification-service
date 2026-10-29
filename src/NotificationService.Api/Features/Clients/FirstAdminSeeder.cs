using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Clients;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Clients;

/// <summary>
/// Creates the first admin client from <c>Bootstrap:AdminApiKey</c> when there is no active admin, so a fresh
/// installation can be used without touching the database. The key itself is never written anywhere.
/// </summary>
internal static partial class FirstAdminSeeder
{
    public const string ClientName = "Operations";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(FirstAdminSeeder));
        var key = configuration["Bootstrap:AdminApiKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        if (await db.ApiClients.AnyAsync(c => c.Role == ClientRole.Admin && c.RevokedAt == null, cancellationToken))
        {
            return;
        }

        var prefix = ApiKey.PrefixOf(key);
        if (prefix is null)
        {
            throw new InvalidOperationException(
                $"Bootstrap:AdminApiKey must look like '{ApiKey.Scheme}<8 lowercase letters or digits>_<at least {ApiKey.MinSecretLength} characters>'.");
        }

        if (await db.ApiClients.AnyAsync(c => c.KeyPrefix == prefix, cancellationToken))
        {
            LogKeyRevoked(logger);
            return;
        }

        var name = await db.ApiClients.AnyAsync(c => c.Name == ClientName, cancellationToken) ? $"{ClientName} {prefix}" : ClientName;
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        db.ApiClients.Add(ApiClient.WithKey(name, ClientRole.Admin, key, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        LogCreated(logger, prefix);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the first admin client (key prefix {KeyPrefix})")]
    private static partial void LogCreated(ILogger logger, string keyPrefix);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The bootstrap admin key was revoked earlier; configure a new one to create an admin")]
    private static partial void LogKeyRevoked(ILogger logger);
}
