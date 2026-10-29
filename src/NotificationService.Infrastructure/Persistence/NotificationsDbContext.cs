using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Clients;
using NotificationService.Core.Notifications;
using NotificationService.Core.Outbox;
using NotificationService.Core.Preferences;
using NotificationService.Core.Templates;
using NotificationService.Infrastructure.Delivery;

namespace NotificationService.Infrastructure.Persistence;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    public DbSet<Template> Templates => Set<Template>();

    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();

    public DbSet<ChannelPreference> ChannelPreferences => Set<ChannelPreference>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
}
