using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Core.Outbox;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.IntegrationTests.Support;

/// <summary>Reads the database directly, for facts the API does not expose.</summary>
public static class Direct
{
    public static async Task<T> DbAsync<T>(this ApiFactory factory, Func<NotificationsDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>());
    }

    /// <summary>The outbox messages that announce one notification.</summary>
    public static async Task<List<OutboxMessage>> OutboxForAsync(this ApiFactory factory, Guid notificationId)
    {
        var all = await factory.DbAsync(db => db.OutboxMessages.AsNoTracking().ToListAsync());
        return all.Where(m => m.Payload.Contains(notificationId.ToString(), StringComparison.Ordinal)).ToList();
    }
}
