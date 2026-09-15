using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One.Domain.Entities;

namespace One.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.Property(t => t.Name).HasMaxLength(160).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(80).IsRequired();
        builder.Property(t => t.LegalName).HasMaxLength(200);
        builder.Property(t => t.TaxId).HasMaxLength(50);
        builder.Property(t => t.ContactEmail).HasMaxLength(256);
        builder.Property(t => t.ContactPhone).HasMaxLength(50);
        builder.Property(t => t.Website).HasMaxLength(300);
        builder.Property(t => t.LogoUrl).HasMaxLength(500);
        builder.Property(t => t.BrandColor).HasMaxLength(20);
        builder.Property(t => t.Country).HasMaxLength(100);
        builder.Property(t => t.City).HasMaxLength(100);
        builder.Property(t => t.Address).HasMaxLength(300);
        builder.Property(t => t.Plan).HasMaxLength(60);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.Property(t => t.Status).HasConversion<int>();

        builder.HasIndex(t => t.Slug).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(t => t.Status);

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public sealed class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> builder)
    {
        builder.ToTable("TenantUsers");

        builder.Property(m => m.Role).HasConversion<int>();

        builder.HasOne(m => m.Tenant)
            .WithMany(t => t.Members)
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);

        // El filtro del tenant se propaga a la pertenencia para no exponer empresas borradas.
        builder.HasQueryFilter(m => !m.Tenant.IsDeleted);
    }
}
