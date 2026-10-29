using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Core.Clients;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates;

namespace NotificationService.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public const string IdempotencyIndex = "ux_notifications_client_idempotency_key";

    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.IdempotencyKey).HasMaxLength(Notification.MaxIdempotencyKeyLength);
        builder.Property(n => n.RequestHash).HasMaxLength(32);
        builder.Property(n => n.RecipientId).HasMaxLength(Recipient.MaxIdLength);
        builder.Property(n => n.RecipientEmail).HasMaxLength(Recipient.MaxEmailLength);
        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.TemplateKey).HasMaxLength(Template.MaxKeyLength);
        builder.Property(n => n.Language).HasMaxLength(2);
        builder.Property(n => n.Subject).HasMaxLength(1_000);
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.LastError).HasMaxLength(Notification.MaxErrorLength);
        builder.Property(n => n.Version).IsRowVersion();

        // The same key from the same client is the same request; the database decides, even under a race.
        builder.HasIndex(n => new { n.ClientId, n.IdempotencyKey }).IsUnique().HasDatabaseName(IdempotencyIndex);
        // A recipient's notifications, newest first: the inbox and the list.
        builder.HasIndex(n => new { n.ClientId, n.RecipientId, n.CreatedAt });
        // Dead letters and anything stuck.
        builder.HasIndex(n => new { n.Status, n.UpdatedAt });

        builder.HasOne<ApiClient>().WithMany().HasForeignKey(n => n.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TemplateVersion>().WithMany().HasForeignKey(n => n.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(n => n.Events).WithOne().HasForeignKey(e => e.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(n => n.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class NotificationEventConfiguration : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("notification_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Detail).HasMaxLength(NotificationEvent.MaxDetailLength);
        builder.HasIndex(e => new { e.NotificationId, e.Id });
    }
}
