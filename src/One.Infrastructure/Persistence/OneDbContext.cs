using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using One.Domain.Entities;
using One.Domain.Identity;

namespace One.Infrastructure.Persistence;

/// <summary>
/// Contexto único de la plataforma: extiende el modelo de Identity (AspNetUsers y compañía)
/// con las tablas propias de empresas, apps, credenciales y configuración.
/// </summary>
public class OneDbContext(DbContextOptions<OneDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<AppDefinition> Apps => Set<AppDefinition>();
    public DbSet<AppSettingDefinition> AppSettingDefinitions => Set<AppSettingDefinition>();
    public DbSet<TenantApp> TenantApps => Set<TenantApp>();
    public DbSet<TenantAppSetting> TenantAppSettings => Set<TenantAppSetting>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(OneDbContext).Assembly);
    }
}
