using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Entities;
using One.Domain.Enums;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>
/// Punto de entrada de las apps integradas: a partir de su api key y secreto resuelve
/// la empresa, la suscripción y todas las variables del entorno de esa credencial.
/// </summary>
public sealed class IntegrationService(
    OneDbContext db,
    ICredentialGenerator generator,
    ISecretProtector protector,
    IAuditService audit,
    ILogger<IntegrationService> logger) : IIntegrationService
{
    public async Task<OperationResult<AppConfigurationDto>> ResolveConfigurationAsync(
        string apiKey, string apiSecret, string? ipAddress, CancellationToken ct = default)
    {
        var auth = await AuthenticateAsync(apiKey, apiSecret, ipAddress, ct);
        if (!auth.Succeeded) return OperationResult<AppConfigurationDto>.Fail(auth.Kind, auth.Message!);

        var credential = auth.Value!;
        var subscription = credential.TenantApp;

        var settings = await db.TenantAppSettings.AsNoTracking()
            .Where(s => s.TenantAppId == subscription.Id && s.Environment == credential.Environment)
            .ToListAsync(ct);

        var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);

        // Los valores por defecto del catálogo actúan como respaldo: una variable que la
        // empresa no personalizó debe llegar igualmente a la app.
        foreach (var definition in subscription.App.SettingDefinitions.Where(d => !d.IsSecret && d.DefaultValue is not null))
            resolved[definition.Key] = definition.DefaultValue;

        foreach (var setting in settings)
        {
            if (setting.IsEncrypted && !string.IsNullOrEmpty(setting.Value))
            {
                if (protector.TryUnprotect(setting.Value, out var plain))
                {
                    resolved[setting.Key] = plain;
                }
                else
                {
                    logger.LogError("No se pudo descifrar la variable {Key} de la suscripción {TenantAppId}", setting.Key, subscription.Id);
                    resolved[setting.Key] = null;
                }
            }
            else if (setting.Value is not null)
            {
                resolved[setting.Key] = setting.Value;
            }
        }

        await RegisterUsageAsync(credential, ipAddress, ct);

        var payload = new AppConfigurationDto(
            new IntegrationTenantDto(
                subscription.Tenant.Id, subscription.Tenant.Name, subscription.Tenant.Slug,
                subscription.Tenant.LogoUrl, subscription.Tenant.BrandColor, subscription.Tenant.Country,
                subscription.Tenant.ContactEmail, subscription.Tenant.Status),
            new IntegrationAppDto(
                subscription.App.Id, subscription.App.Name, subscription.App.Slug,
                subscription.DisplayName, subscription.App.Version, subscription.App.Category),
            credential.Environment,
            credential.ClientId,
            Mappings.SplitScopes(credential.Scopes ?? subscription.GrantedScopes),
            Mappings.SplitLines(subscription.AllowedOrigins),
            resolved,
            subscription.ExpiresAt,
            DateTimeOffset.UtcNow,
            ComputeVersion(resolved, subscription.UpdatedAt ?? subscription.CreatedAt));

        return OperationResult<AppConfigurationDto>.Ok(payload);
    }

    public async Task<OperationResult<CredentialIntrospectionDto>> IntrospectAsync(
        string apiKey, string apiSecret, string? ipAddress, CancellationToken ct = default)
    {
        var auth = await AuthenticateAsync(apiKey, apiSecret, ipAddress, ct);
        if (!auth.Succeeded) return OperationResult<CredentialIntrospectionDto>.Fail(auth.Kind, auth.Message!);

        var credential = auth.Value!;
        var subscription = credential.TenantApp;

        await RegisterUsageAsync(credential, ipAddress, ct);

        return OperationResult<CredentialIntrospectionDto>.Ok(new CredentialIntrospectionDto(
            true,
            credential.Id,
            credential.ClientId,
            subscription.TenantId,
            subscription.Tenant.Slug,
            subscription.AppId,
            subscription.App.Slug,
            credential.Environment,
            Mappings.SplitScopes(credential.Scopes ?? subscription.GrantedScopes),
            credential.ExpiresAt));
    }

    private async Task<OperationResult<ApiCredential>> AuthenticateAsync(
        string apiKey, string apiSecret, string? ipAddress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            return OperationResult<ApiCredential>.Unauthorized("Debe enviar X-Api-Key y X-Api-Secret.");

        var keyHash = generator.Hash(apiKey.Trim());

        var credential = await db.ApiCredentials
            .Include(c => c.TenantApp).ThenInclude(s => s.Tenant)
            .Include(c => c.TenantApp).ThenInclude(s => s.App).ThenInclude(a => a.SettingDefinitions)
            .FirstOrDefaultAsync(c => c.ApiKeyHash == keyHash, ct);

        if (credential is null)
        {
            await audit.LogAsync("integration.auth_failed", nameof(ApiCredential), null, metadata:
                new { Reason = "api key desconocida", Ip = ipAddress }, success: false,
                errorMessage: "Api key no reconocida", ct: ct);

            return OperationResult<ApiCredential>.Unauthorized("Credenciales inválidas.");
        }

        if (!generator.Verify(apiSecret.Trim(), credential.SecretHash))
        {
            await audit.LogAsync("integration.auth_failed", nameof(ApiCredential), credential.Id.ToString(),
                credential.TenantApp.TenantId, new { Reason = "secreto incorrecto", Ip = ipAddress },
                success: false, errorMessage: "Secreto incorrecto", ct: ct);

            return OperationResult<ApiCredential>.Unauthorized("Credenciales inválidas.");
        }

        if (credential.Status == CredentialStatus.Revoked)
            return Deny(credential, "La credencial fue revocada.");

        if (credential.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            if (credential.Status != CredentialStatus.Expired)
            {
                credential.Status = CredentialStatus.Expired;
                await db.SaveChangesAsync(ct);
            }

            return Deny(credential, "La credencial caducó.");
        }

        var subscription = credential.TenantApp;

        if (!subscription.IsEnabled || subscription.Status != SubscriptionStatus.Active)
            return Deny(credential, "La aplicación está deshabilitada para esta empresa.");

        if (subscription.ExpiresAt is { } subExpiry && subExpiry <= DateTimeOffset.UtcNow)
            return Deny(credential, "La suscripción de la empresa a esta aplicación caducó.");

        if (subscription.Tenant.Status is TenantStatus.Suspended or TenantStatus.Archived)
            return Deny(credential, "La empresa no está activa.");

        if (!subscription.App.IsActive)
            return Deny(credential, "La aplicación está desactivada en el catálogo.");

        if (!IsIpAllowed(credential.AllowedIps, ipAddress))
            return Deny(credential, "La dirección IP de origen no está autorizada para esta credencial.");

        return OperationResult<ApiCredential>.Ok(credential);
    }

    private static OperationResult<ApiCredential> Deny(ApiCredential credential, string reason) =>
        OperationResult<ApiCredential>.Forbidden(reason);

    private async Task RegisterUsageAsync(ApiCredential credential, string? ipAddress, CancellationToken ct)
    {
        // Actualización directa para no arrastrar el grafo cargado ni provocar
        // conflictos de concurrencia entre llamadas simultáneas de la misma app.
        await db.ApiCredentials.IgnoreQueryFilters()
            .Where(c => c.Id == credential.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LastUsedAt, DateTimeOffset.UtcNow)
                .SetProperty(c => c.LastUsedIp, ipAddress)
                .SetProperty(c => c.UsageCount, c => c.UsageCount + 1), ct);
    }

    /// <summary>
    /// Lista blanca opcional. Admite direcciones exactas y notación CIDR;
    /// vacío significa sin restricción de origen.
    /// </summary>
    private static bool IsIpAllowed(string? allowedIps, string? ipAddress)
    {
        var rules = Mappings.SplitLines(allowedIps);
        if (rules.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var address)) return false;

        foreach (var rule in rules)
        {
            if (IPAddress.TryParse(rule, out var exact) && exact.Equals(address)) return true;

            var slash = rule.IndexOf('/');
            if (slash <= 0) continue;

            if (!IPAddress.TryParse(rule[..slash], out var network)) continue;
            if (!int.TryParse(rule[(slash + 1)..], out var prefix)) continue;
            if (network.AddressFamily != address.AddressFamily) continue;

            if (IsInSubnet(address, network, prefix)) return true;
        }

        return false;
    }

    private static bool IsInSubnet(IPAddress address, IPAddress network, int prefixLength)
    {
        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        if (addressBytes.Length != networkBytes.Length) return false;
        if (prefixLength < 0 || prefixLength > addressBytes.Length * 8) return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
            if (addressBytes[i] != networkBytes[i]) return false;

        if (remainingBits == 0) return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }

    /// <summary>
    /// Huella estable del payload. La app puede cachear su configuración y volver a
    /// pedirla solo cuando cambia esta versión.
    /// </summary>
    private static string ComputeVersion(IDictionary<string, string?> settings, DateTimeOffset updatedAt)
    {
        var builder = new StringBuilder(updatedAt.ToUnixTimeSeconds().ToString());

        foreach (var pair in settings.OrderBy(p => p.Key, StringComparer.Ordinal))
            builder.Append('|').Append(pair.Key).Append('=').Append(pair.Value ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
    }
}
