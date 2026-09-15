using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One.Domain.Entities;

namespace One.Infrastructure.Persistence.Configurations;

public sealed class AppDefinitionConfiguration : IEntityTypeConfiguration<AppDefinition>
{
    public void Configure(EntityTypeBuilder<AppDefinition> builder)
    {
        builder.ToTable("Apps");

        builder.Property(a => a.Name).HasMaxLength(160).IsRequired();
        builder.Property(a => a.Slug).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.Property(a => a.Category).HasMaxLength(80);
        builder.Property(a => a.IconUrl).HasMaxLength(500);
        builder.Property(a => a.Color).HasMaxLength(20);
        builder.Property(a => a.Version).HasMaxLength(40);
        builder.Property(a => a.HomepageUrl).HasMaxLength(300);
        builder.Property(a => a.DocumentationUrl).HasMaxLength(300);
        builder.Property(a => a.SupportEmail).HasMaxLength(256);
        builder.Property(a => a.AvailableScopes).HasMaxLength(1000);

        builder.HasIndex(a => a.Slug).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(a => a.Category);

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public sealed class AppSettingDefinitionConfiguration : IEntityTypeConfiguration<AppSettingDefinition>
{
    public void Configure(EntityTypeBuilder<AppSettingDefinition> builder)
    {
        builder.ToTable("AppSettingDefinitions");

        builder.Property(d => d.Key).HasMaxLength(120).IsRequired();
        builder.Property(d => d.Label).HasMaxLength(160).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(1000);
        builder.Property(d => d.Placeholder).HasMaxLength(200);
        builder.Property(d => d.DefaultValue).HasMaxLength(2000);
        builder.Property(d => d.ValidationRegex).HasMaxLength(500);
        builder.Property(d => d.AllowedValues).HasMaxLength(2000);
        builder.Property(d => d.Group).HasMaxLength(80);
        builder.Property(d => d.DataType).HasConversion<int>();

        builder.HasOne(d => d.App)
            .WithMany(a => a.SettingDefinitions)
            .HasForeignKey(d => d.AppId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.AppId, d.Key }).IsUnique();

        builder.HasQueryFilter(d => !d.App.IsDeleted);
    }
}
