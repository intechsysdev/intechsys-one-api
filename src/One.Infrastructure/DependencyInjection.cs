using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using One.Application.Contracts;
using One.Domain.Identity;
using One.Infrastructure.Options;
using One.Infrastructure.Persistence;
using One.Infrastructure.Security;
using One.Infrastructure.Services;

namespace One.Infrastructure;

/// <summary>Registro de la persistencia, Identity y los servicios de aplicación.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOneInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Default.");

        services.AddDbContext<OneDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(60);
            });

            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<OneDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<ICredentialGenerator, CredentialGenerator>();
        services.AddSingleton<ITokenService, TokenService>();

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IAppService, AppService>();
        services.AddScoped<ITenantAppService, TenantAppService>();
        services.AddScoped<ISettingService, SettingService>();
        services.AddScoped<ICredentialService, CredentialService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IIntegrationService, IntegrationService>();
        services.AddScoped<IDashboardService, DashboardService>();

        services.AddScoped<DbSeeder>();

        return services;
    }
}
