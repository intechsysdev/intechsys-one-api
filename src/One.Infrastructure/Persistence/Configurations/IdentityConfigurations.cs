using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One.Domain.Identity;

namespace One.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("AspNetUsers");

        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.AvatarUrl).HasMaxLength(500);
        builder.Property(u => u.JobTitle).HasMaxLength(150);
        builder.Property(u => u.TimeZone).HasMaxLength(60).IsRequired();
        builder.Property(u => u.Locale).HasMaxLength(20).IsRequired();
        builder.Property(u => u.LastLoginIp).HasMaxLength(64);

        builder.Ignore(u => u.FullName);

        builder.HasIndex(u => u.IsActive);
        builder.HasIndex(u => u.CreatedAt);
    }
}

public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.ToTable("AspNetRoles");
        builder.Property(r => r.Description).HasMaxLength(300);
    }
}
