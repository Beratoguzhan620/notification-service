using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Core.Outbox;

namespace NotificationService.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.TraceParent).HasMaxLength(100);
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
        // The relay only ever looks at unpublished rows; the index stays small however long the table grows.
        builder.HasIndex(m => m.CreatedAt).HasFilter("published_at IS NULL").HasDatabaseName("ix_outbox_messages_unpublished");
    }
}
