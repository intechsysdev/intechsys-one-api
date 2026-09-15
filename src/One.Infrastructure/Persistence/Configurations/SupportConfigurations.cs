using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One.Domain.Entities;

namespace One.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);
        builder.Property(t => t.CreatedByIp).HasMaxLength(64);
        builder.Property(t => t.RevokedByIp).HasMaxLength(64);
        builder.Property(t => t.UserAgent).HasMaxLength(400);

        builder.Ignore(t => t.IsActive);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.ExpiresAt);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.Property(l => l.Action).HasMaxLength(120).IsRequired();
        builder.Property(l => l.ActorName).HasMaxLength(256);
        builder.Property(l => l.EntityType).HasMaxLength(120);
        builder.Property(l => l.EntityId).HasMaxLength(80);
        builder.Property(l => l.Metadata).HasMaxLength(4000);
        builder.Property(l => l.IpAddress).HasMaxLength(64);
        builder.Property(l => l.UserAgent).HasMaxLength(400);
        builder.Property(l => l.ErrorMessage).HasMaxLength(1000);

        builder.HasIndex(l => l.CreatedAt).IsDescending();
        builder.HasIndex(l => new { l.TenantId, l.CreatedAt });
        builder.HasIndex(l => l.Action);
    }
}
