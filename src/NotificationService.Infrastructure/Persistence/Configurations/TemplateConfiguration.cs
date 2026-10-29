using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Core.Templates;

namespace NotificationService.Infrastructure.Persistence.Configurations;

internal sealed class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Key).HasMaxLength(Template.MaxKeyLength);
        builder.Property(t => t.Description).HasMaxLength(Template.MaxDescriptionLength);
        builder.Property(t => t.DefaultLanguage).HasMaxLength(2);
        builder.HasIndex(t => t.Key).IsUnique();

        builder.HasMany(t => t.Versions).WithOne().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TemplateVersionConfiguration : IEntityTypeConfiguration<TemplateVersion>
{
    public void Configure(EntityTypeBuilder<TemplateVersion> builder)
    {
        builder.ToTable("template_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Language).HasMaxLength(2);
        builder.Property(v => v.Subject).HasMaxLength(TemplateVersion.MaxSubjectLength);
        builder.Property(v => v.Text).HasMaxLength(TemplateVersion.MaxTextLength);
        builder.Property(v => v.Html).HasMaxLength(TemplateVersion.MaxHtmlLength);
        builder.Ignore(v => v.Variables);
        // Two admins adding a version at the same moment cannot both get the same number.
        builder.HasIndex(v => new { v.TemplateId, v.Language, v.Version }).IsUnique();
    }
}
