using Microsoft.EntityFrameworkCore;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Entities;
using One.Domain.Enums;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>Asignación de apps del catálogo a una empresa y estado de cada suscripción.</summary>
public sealed class TenantAppService(
    OneDbContext db,
    IAuditService audit,
    ICurrentUser currentUser,
    ICredentialGenerator credentials,
    ISecretProtector protector) : ITenantAppService
{
    public async Task<OperationResult<IReadOnlyList<TenantAppDto>>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            return OperationResult<IReadOnlyList<TenantAppDto>>.NotFound("La empresa no existe.");

        var rows = await db.TenantApps.AsNoTracking()
            .Include(s => s.Tenant)
            .Include(s => s.App)
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.App.Name)
            .Select(s => new
            {
                Subscription = s,
                CredentialCount = s.Credentials.Count(),
                ActiveCredentials = s.Credentials.Count(c => c.Status == CredentialStatus.Active),
                SettingCount = s.Settings.Count(),
                RequiredKeys = s.App.SettingDefinitions.Where(d => d.IsRequired).Select(d => d.Key).ToList(),
                FilledKeys = s.Settings.Where(v => v.Value != null && v.Value != "").Select(v => v.Key).ToList()
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => r.Subscription.ToDto(
                r.CredentialCount,
                r.ActiveCredentials,
                r.SettingCount,
                r.RequiredKeys.Count(k => !r.FilledKeys.Contains(k))))
            .ToList();

        return OperationResult<IReadOnlyList<TenantAppDto>>.Ok(items);
    }

    public async Task<OperationResult<TenantAppDetailDto>> GetAsync(Guid tenantId, Guid tenantAppId, bool revealSecrets, CancellationToken ct = default)
    {
        var subscription = await db.TenantApps.AsNoTracking()
            .Include(s => s.Tenant)
            .Include(s => s.App).ThenInclude(a => a.SettingDefinitions)
            .Include(s => s.Settings)
            .Include(s => s.Credentials)
            .FirstOrDefaultAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct);

        if (subscription is null)
            return OperationResult<TenantAppDetailDto>.NotFound("La app no está asignada a esta empresa.");

        var requiredKeys = subscription.App.SettingDefinitions.Where(d => d.IsRequired).Select(d => d.Key).ToHashSet();
        var filledKeys = subscription.Settings.Where(v => !string.IsNullOrEmpty(v.Value)).Select(v => v.Key).ToHashSet();

        var dto = subscription.ToDto(
            subscription.Credentials.Count,
            subscription.Credentials.Count(c => c.Status == CredentialStatus.Active),
            subscription.Settings.Count,
            requiredKeys.Count(k => !filledKeys.Contains(k)));

        var settings = subscription.Settings
            .OrderBy(s => s.Environment).ThenBy(s => s.Key)
            .Select(s => s.ToDto(ResolveValue(s, revealSecrets), revealSecrets))
            .ToList();

        return OperationResult<TenantAppDetailDto>.Ok(new TenantAppDetailDto(
            dto,
            [.. subscription.App.SettingDefinitions.OrderBy(d => d.DisplayOrder).ThenBy(d => d.Key).Select(d => d.ToDto())],
            settings,
            [.. subscription.Credentials.OrderByDescending(c => c.CreatedAt).Select(c => c.ToDto())]));
    }

    public async Task<OperationResult<TenantAppDto>> AssignAsync(Guid tenantId, AssignAppRequest request, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return OperationResult<TenantAppDto>.NotFound("La empresa no existe.");

        var app = await db.Apps.Include(a => a.SettingDefinitions).FirstOrDefaultAsync(a => a.Id == request.AppId, ct);
        if (app is null) return OperationResult<TenantAppDto>.NotFound("La aplicación no existe.");

        if (!app.IsActive)
            return OperationResult<TenantAppDto>.Invalid("La aplicación está desactivada en el catálogo.");

        if (await db.TenantApps.AnyAsync(s => s.TenantId == tenantId && s.AppId == request.AppId, ct))
            return OperationResult<TenantAppDto>.Conflict("La empresa ya tiene asignada esta aplicación.");

        if (tenant.MaxApps is { } max && await db.TenantApps.CountAsync(s => s.TenantId == tenantId, ct) >= max)
            return OperationResult<TenantAppDto>.Conflict($"La empresa alcanzó su límite de {max} aplicaciones.");

        var subscription = new TenantApp
        {
            TenantId = tenantId,
            AppId = request.AppId,
            DisplayName = request.DisplayName?.Trim(),
            IsEnabled = request.IsEnabled,
            Status = SubscriptionStatus.Active,
            ExpiresAt = request.ExpiresAt,
            GrantedScopes = request.GrantedScopes?.Trim() ?? app.AvailableScopes,
            AllowedOrigins = request.AllowedOrigins?.Trim(),
            WebhookUrl = request.WebhookUrl?.Trim(),
            Notes = request.Notes?.Trim(),
            CreatedBy = currentUser.UserId
        };

        db.TenantApps.Add(subscription);

        if (request.SeedDefaultSettings)
        {
            // Sembrar el esquema deja el formulario del portal listo para rellenar
            // en lugar de en blanco, y marca de una vez cuáles son obligatorias.
            foreach (var definition in app.SettingDefinitions)
            {
                subscription.Settings.Add(new TenantAppSetting
                {
                    TenantAppId = subscription.Id,
                    SettingDefinitionId = definition.Id,
                    Key = definition.Key,
                    Value = definition.IsSecret ? null : definition.DefaultValue,
                    DataType = definition.DataType,
                    IsSecret = definition.IsSecret,
                    IsEncrypted = false,
                    Environment = AppEnvironment.Production,
                    Description = definition.Description,
                    CreatedBy = currentUser.UserId
                });
            }
        }

        if (request.CreateCredentialForEnvironment is { } environment)
        {
            var generated = credentials.Generate(environment);
            subscription.Credentials.Add(new ApiCredential
            {
                TenantAppId = subscription.Id,
                Name = $"Credencial inicial · {environment}",
                Environment = environment,
                ClientId = generated.ClientId,
                KeyPrefix = generated.KeyPrefix,
                ApiKeyHash = generated.ApiKeyHash,
                SecretHash = generated.SecretHash,
                SecretLast4 = generated.SecretLast4,
                Scopes = subscription.GrantedScopes,
                CreatedBy = currentUser.UserId
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("tenant_app.assigned", nameof(TenantApp), subscription.Id.ToString(), tenantId,
            new { app.Slug, Settings = subscription.Settings.Count }, ct: ct);

        var result = await ListForTenantAsync(tenantId, ct);
        var created = result.Value?.FirstOrDefault(s => s.Id == subscription.Id);

        return created is null
            ? OperationResult<TenantAppDto>.NotFound("No se pudo leer la asignación recién creada.")
            : OperationResult<TenantAppDto>.Ok(created);
    }

    public async Task<OperationResult<TenantAppDto>> UpdateAsync(Guid tenantId, Guid tenantAppId, UpdateTenantAppRequest request, CancellationToken ct = default)
    {
        var subscription = await db.TenantApps
            .Include(s => s.Tenant)
            .Include(s => s.App)
            .FirstOrDefaultAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct);

        if (subscription is null)
            return OperationResult<TenantAppDto>.NotFound("La app no está asignada a esta empresa.");

        subscription.DisplayName = request.DisplayName?.Trim();
        subscription.IsEnabled = request.IsEnabled;
        subscription.Status = request.Status;
        subscription.ExpiresAt = request.ExpiresAt;
        subscription.GrantedScopes = request.GrantedScopes?.Trim();
        subscription.AllowedOrigins = request.AllowedOrigins?.Trim();
        subscription.WebhookUrl = request.WebhookUrl?.Trim();
        subscription.Notes = request.Notes?.Trim();
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        subscription.UpdatedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("tenant_app.updated", nameof(TenantApp), tenantAppId.ToString(), tenantId,
            new { subscription.IsEnabled, Status = subscription.Status.ToString() }, ct: ct);

        var result = await ListForTenantAsync(tenantId, ct);
        var updated = result.Value?.FirstOrDefault(s => s.Id == tenantAppId);

        return updated is null
            ? OperationResult<TenantAppDto>.NotFound("No se pudo leer la asignación actualizada.")
            : OperationResult<TenantAppDto>.Ok(updated);
    }

    public async Task<OperationResult> RemoveAsync(Guid tenantId, Guid tenantAppId, CancellationToken ct = default)
    {
        var subscription = await db.TenantApps
            .Include(s => s.App)
            .FirstOrDefaultAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct);

        if (subscription is null) return OperationResult.NotFound("La app no está asignada a esta empresa.");

        db.TenantApps.Remove(subscription);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("tenant_app.removed", nameof(TenantApp), tenantAppId.ToString(), tenantId,
            new { subscription.App.Slug }, ct: ct);

        return OperationResult.Ok();
    }

    private string? ResolveValue(TenantAppSetting setting, bool reveal)
    {
        if (!setting.IsEncrypted || string.IsNullOrEmpty(setting.Value)) return setting.Value;
        if (!reveal) return setting.Value;

        return protector.TryUnprotect(setting.Value, out var plain) ? plain : null;
    }
}
