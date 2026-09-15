using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using One.Application.Contracts;
using One.Domain.Entities;
using One.Domain.Enums;
using One.Domain.Identity;
using One.Infrastructure.Options;

namespace One.Infrastructure.Persistence;

/// <summary>
/// Prepara la base en el primer arranque: migraciones, roles de plataforma, cuenta de
/// administrador y, opcionalmente, una empresa y una app de ejemplo para explorar el portal.
/// </summary>
public sealed class DbSeeder(
    OneDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ICredentialGenerator credentials,
    IOptions<SeedOptions> options,
    ILogger<DbSeeder> logger)
{
    private readonly SeedOptions _options = options.Value;

    public async Task RunAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!_options.Enabled)
        {
            logger.LogInformation("Seed desactivado por configuración.");
            return;
        }

        await SeedRolesAsync();
        var admin = await SeedAdminAsync();

        if (_options.IncludeDemoData)
            await SeedDemoDataAsync(admin, ct);
    }

    private async Task SeedRolesAsync()
    {
        foreach (var (name, description) in PlatformRoles.All)
        {
            if (await roleManager.RoleExistsAsync(name)) continue;

            var result = await roleManager.CreateAsync(new ApplicationRole(name)
            {
                Description = description,
                IsSystem = true
            });

            if (result.Succeeded)
                logger.LogInformation("Rol de plataforma creado: {Role}", name);
            else
                logger.LogError("No se pudo crear el rol {Role}: {Errors}", name, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task<ApplicationUser> SeedAdminAsync()
    {
        var email = _options.AdminEmail.Trim().ToLowerInvariant();
        var existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            if (!await userManager.IsInRoleAsync(existing, PlatformRoles.PlatformAdmin))
                await userManager.AddToRoleAsync(existing, PlatformRoles.PlatformAdmin);

            return existing;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = _options.AdminFirstName,
            LastName = _options.AdminLastName,
            JobTitle = "Administrador de plataforma",
            IsActive = true,
            MustChangePassword = true
        };

        var created = await userManager.CreateAsync(admin, _options.AdminPassword);

        if (!created.Succeeded)
            throw new InvalidOperationException(
                $"No se pudo crear el administrador inicial: {string.Join("; ", created.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(admin, PlatformRoles.PlatformAdmin);
        logger.LogWarning("Administrador inicial creado: {Email}. Cambie la contraseña en el primer acceso.", email);

        return admin;
    }

    private async Task SeedDemoDataAsync(ApplicationUser admin, CancellationToken ct)
    {
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(ct)) return;

        var tenant = new Tenant
        {
            Name = "Intechsys",
            Slug = "intechsys",
            LegalName = "Intechsys S.A.S.",
            ContactEmail = admin.Email,
            Country = "Colombia",
            City = "Bogotá",
            BrandColor = "#6366f1",
            Status = TenantStatus.Active,
            Plan = "Enterprise",
            CreatedBy = admin.Id
        };

        db.Tenants.Add(tenant);
        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            UserId = admin.Id,
            Role = TenantRole.Owner,
            IsDefault = true,
            JoinedAt = DateTimeOffset.UtcNow,
            CreatedBy = admin.Id
        });

        var app = new AppDefinition
        {
            Name = "Pedidos",
            Slug = "pedidos",
            Description = "Gestión de pedidos y despachos integrada con el ERP.",
            Category = "Operaciones",
            Color = "#0ea5e9",
            Version = "1.0.0",
            IsActive = true,
            IsPublic = true,
            AvailableScopes = "config.read orders.read orders.write",
            CreatedBy = admin.Id
        };

        var definitions = new[]
        {
            new AppSettingDefinition
            {
                AppId = app.Id, Key = "API_BASE_URL", Label = "URL base del ERP",
                Description = "Endpoint contra el que la app publica los pedidos.",
                Placeholder = "https://erp.empresa.com/api",
                DataType = SettingDataType.Url, IsRequired = true, Group = "Conexión", DisplayOrder = 1
            },
            new AppSettingDefinition
            {
                AppId = app.Id, Key = "ERP_TOKEN", Label = "Token del ERP",
                Description = "Credencial de servicio emitida por el ERP.",
                DataType = SettingDataType.Secret, IsRequired = true, IsSecret = true,
                Group = "Conexión", DisplayOrder = 2
            },
            new AppSettingDefinition
            {
                AppId = app.Id, Key = "MAX_ITEMS_PER_ORDER", Label = "Máximo de ítems por pedido",
                DataType = SettingDataType.Number, DefaultValue = "50",
                Group = "Reglas de negocio", DisplayOrder = 3
            },
            new AppSettingDefinition
            {
                AppId = app.Id, Key = "ENABLE_NOTIFICATIONS", Label = "Notificar por correo",
                DataType = SettingDataType.Boolean, DefaultValue = "true",
                Group = "Notificaciones", DisplayOrder = 4
            }
        };

        foreach (var definition in definitions)
            app.SettingDefinitions.Add(definition);

        db.Apps.Add(app);

        var subscription = new TenantApp
        {
            TenantId = tenant.Id,
            AppId = app.Id,
            IsEnabled = true,
            Status = SubscriptionStatus.Active,
            GrantedScopes = app.AvailableScopes,
            AllowedOrigins = "https://localhost:5173",
            CreatedBy = admin.Id
        };

        foreach (var definition in definitions)
        {
            subscription.Settings.Add(new TenantAppSetting
            {
                TenantAppId = subscription.Id,
                SettingDefinitionId = definition.Id,
                Key = definition.Key,
                Value = definition.IsSecret ? null : definition.DefaultValue,
                DataType = definition.DataType,
                IsSecret = definition.IsSecret,
                Environment = AppEnvironment.Production,
                Description = definition.Description,
                CreatedBy = admin.Id
            });
        }

        var generated = credentials.Generate(AppEnvironment.Development);
        subscription.Credentials.Add(new ApiCredential
        {
            TenantAppId = subscription.Id,
            Name = "Entorno local",
            Environment = AppEnvironment.Development,
            ClientId = generated.ClientId,
            KeyPrefix = generated.KeyPrefix,
            ApiKeyHash = generated.ApiKeyHash,
            SecretHash = generated.SecretHash,
            SecretLast4 = generated.SecretLast4,
            Scopes = app.AvailableScopes,
            CreatedBy = admin.Id
        });

        db.TenantApps.Add(subscription);
        await db.SaveChangesAsync(ct);

        // Se registran en el log porque es la única vez que existen en claro.
        logger.LogWarning(
            "Datos de ejemplo creados. Credencial de integración para {Tenant}/{App}: ApiKey={ApiKey} ApiSecret={ApiSecret}",
            tenant.Slug, app.Slug, generated.ApiKey, generated.ApiSecret);
    }
}
