using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Core.Clients;

namespace NotificationService.Infrastructure.Persistence.Configurations;

internal sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.ToTable("api_clients");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(ApiClient.MaxNameLength);
        builder.Property(c => c.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.KeyPrefix).HasMaxLength(ApiKey.PrefixLength);
        builder.Property(c => c.KeyHash).HasMaxLength(32);
        builder.HasIndex(c => c.KeyPrefix).IsUnique();
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Ignore(c => c.IsActive);
    }
}
