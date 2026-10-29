using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Clients;

namespace NotificationService.Infrastructure.Persistence;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
}
