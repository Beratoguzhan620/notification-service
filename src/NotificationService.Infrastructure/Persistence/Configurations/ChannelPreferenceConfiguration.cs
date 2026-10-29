using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Core.Clients;
using NotificationService.Core.Notifications;
using NotificationService.Core.Preferences;

namespace NotificationService.Infrastructure.Persistence.Configurations;

internal sealed class ChannelPreferenceConfiguration : IEntityTypeConfiguration<ChannelPreference>
{
    public void Configure(EntityTypeBuilder<ChannelPreference> builder)
    {
        builder.ToTable("channel_preferences");
        builder.HasKey(p => new { p.ClientId, p.RecipientId, p.Channel });
        builder.Property(p => p.RecipientId).HasMaxLength(Recipient.MaxIdLength);
        builder.Property(p => p.Channel).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<ApiClient>().WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
    }
}
