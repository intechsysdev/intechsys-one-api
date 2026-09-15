using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One.Domain.Entities;

namespace One.Infrastructure.Persistence.Configurations;

public sealed class TenantAppConfiguration : IEntityTypeConfiguration<TenantApp>
{
    public void Configure(EntityTypeBuilder<TenantApp> builder)
    {
        builder.ToTable("TenantApps");

        builder.Property(s => s.DisplayName).HasMaxLength(160);
        builder.Property(s => s.GrantedScopes).HasMaxLength(1000);
        builder.Property(s => s.AllowedOrigins).HasMaxLength(2000);
        builder.Property(s => s.WebhookUrl).HasMaxLength(500);
        builder.Property(s => s.Notes).HasMaxLength(2000);
        builder.Property(s => s.Status).HasConversion<int>();

        builder.HasOne(s => s.Tenant)
            .WithMany(t => t.Apps)
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restringido: una app con empresas activas no se puede borrar físicamente.
        builder.HasOne(s => s.App)
            .WithMany(a => a.Subscriptions)
            .HasForeignKey(s => s.AppId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.TenantId, s.AppId }).IsUnique();
        builder.HasIndex(s => s.AppId);

        builder.HasQueryFilter(s => !s.Tenant.IsDeleted && !s.App.IsDeleted);
    }
}

public sealed class TenantAppSettingConfiguration : IEntityTypeConfiguration<TenantAppSetting>
{
    public void Configure(EntityTypeBuilder<TenantAppSetting> builder)
    {
        builder.ToTable("TenantAppSettings");

        builder.Property(s => s.Key).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(8000);
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.Property(s => s.DataType).HasConversion<int>();
        builder.Property(s => s.Environment).HasConversion<int>();

        builder.HasOne(s => s.TenantApp)
            .WithMany(t => t.Settings)
            .HasForeignKey(s => s.TenantAppId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.SettingDefinition)
            .WithMany()
            .HasForeignKey(s => s.SettingDefinitionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.TenantAppId, s.Key, s.Environment }).IsUnique();

        builder.HasQueryFilter(s => !s.TenantApp.Tenant.IsDeleted && !s.TenantApp.App.IsDeleted);
    }
}

public sealed class ApiCredentialConfiguration : IEntityTypeConfiguration<ApiCredential>
{
    public void Configure(EntityTypeBuilder<ApiCredential> builder)
    {
        builder.ToTable("ApiCredentials");

        builder.Property(c => c.Name).HasMaxLength(160).IsRequired();
        builder.Property(c => c.ClientId).HasMaxLength(80).IsRequired();
        builder.Property(c => c.KeyPrefix).HasMaxLength(40).IsRequired();
        builder.Property(c => c.ApiKeyHash).HasMaxLength(128).IsRequired();
        builder.Property(c => c.SecretHash).HasMaxLength(128).IsRequired();
        builder.Property(c => c.SecretLast4).HasMaxLength(8).IsRequired();
        builder.Property(c => c.Scopes).HasMaxLength(1000);
        builder.Property(c => c.AllowedIps).HasMaxLength(2000);
        builder.Property(c => c.LastUsedIp).HasMaxLength(64);
        builder.Property(c => c.RevokedReason).HasMaxLength(500);
        builder.Property(c => c.Environment).HasConversion<int>();
        builder.Property(c => c.Status).HasConversion<int>();

        builder.Ignore(c => c.IsUsable);

        builder.HasOne(c => c.TenantApp)
            .WithMany(t => t.Credentials)
            .HasForeignKey(c => c.TenantAppId)
            .OnDelete(DeleteBehavior.Cascade);

        // Punto de entrada de la autenticación máquina a máquina: debe resolverse con un seek.
        builder.HasIndex(c => c.ApiKeyHash).IsUnique();
        builder.HasIndex(c => c.ClientId).IsUnique();
        builder.HasIndex(c => new { c.TenantAppId, c.Environment });

        builder.HasQueryFilter(c => !c.TenantApp.Tenant.IsDeleted && !c.TenantApp.App.IsDeleted);
    }
}
