using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Entities;
using One.Domain.Enums;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>
/// Variables de configuración por empresa, app y entorno. Las marcadas como secretas
/// se cifran antes de tocar la base y solo se devuelven en claro bajo petición explícita.
/// </summary>
public sealed class SettingService(
    OneDbContext db,
    ISecretProtector protector,
    IAuditService audit,
    ICurrentUser currentUser) : ISettingService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public async Task<OperationResult<IReadOnlyList<TenantAppSettingDto>>> ListAsync(
        Guid tenantId, Guid tenantAppId, AppEnvironment? environment, bool reveal, CancellationToken ct = default)
    {
        if (!await SubscriptionExistsAsync(tenantId, tenantAppId, ct))
            return OperationResult<IReadOnlyList<TenantAppSettingDto>>.NotFound("La app no está asignada a esta empresa.");

        var query = db.TenantAppSettings.AsNoTracking().Where(s => s.TenantAppId == tenantAppId);

        if (environment is not null)
            query = query.Where(s => s.Environment == environment);

        var settings = await query.OrderBy(s => s.Environment).ThenBy(s => s.Key).ToListAsync(ct);

        if (reveal && settings.Any(s => s.IsSecret))
            await audit.LogAsync("setting.revealed", nameof(TenantApp), tenantAppId.ToString(), tenantId,
                new { Count = settings.Count(s => s.IsSecret) }, ct: ct);

        return OperationResult<IReadOnlyList<TenantAppSettingDto>>.Ok(
            [.. settings.Select(s => s.ToDto(Decrypt(s, reveal), reveal))]);
    }

    public async Task<OperationResult<TenantAppSettingDto>> UpsertAsync(
        Guid tenantId, Guid tenantAppId, UpsertSettingRequest request, CancellationToken ct = default)
    {
        var subscription = await db.TenantApps
            .Include(s => s.App).ThenInclude(a => a.SettingDefinitions)
            .FirstOrDefaultAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct);

        if (subscription is null)
            return OperationResult<TenantAppSettingDto>.NotFound("La app no está asignada a esta empresa.");

        var result = await UpsertCoreAsync(subscription, request, ct);
        if (!result.Succeeded) return result;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("setting.saved", nameof(TenantAppSetting), result.Value!.Id.ToString(), tenantId,
            new { request.Key, request.IsSecret, Environment = request.Environment.ToString() }, ct: ct);

        return result;
    }

    public async Task<OperationResult<IReadOnlyList<TenantAppSettingDto>>> BulkUpsertAsync(
        Guid tenantId, Guid tenantAppId, BulkUpsertSettingsRequest request, CancellationToken ct = default)
    {
        var subscription = await db.TenantApps
            .Include(s => s.App).ThenInclude(a => a.SettingDefinitions)
            .FirstOrDefaultAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct);

        if (subscription is null)
            return OperationResult<IReadOnlyList<TenantAppSettingDto>>.NotFound("La app no está asignada a esta empresa.");

        var duplicated = request.Settings
            .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicated is not null)
            return OperationResult<IReadOnlyList<TenantAppSettingDto>>.Invalid($"La variable {duplicated.Key} aparece más de una vez.");

        var saved = new List<TenantAppSettingDto>(request.Settings.Count);

        foreach (var item in request.Settings)
        {
            var single = item with { Environment = request.Environment };
            var result = await UpsertCoreAsync(subscription, single, ct);

            if (!result.Succeeded)
                return OperationResult<IReadOnlyList<TenantAppSettingDto>>.Fail(result.Kind, result.Message ?? "No se pudo guardar la configuración.", result.Errors);

            saved.Add(result.Value!);
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("setting.bulk_saved", nameof(TenantApp), tenantAppId.ToString(), tenantId,
            new { Count = saved.Count, Environment = request.Environment.ToString() }, ct: ct);

        return OperationResult<IReadOnlyList<TenantAppSettingDto>>.Ok(saved);
    }

    public async Task<OperationResult> DeleteAsync(Guid tenantId, Guid tenantAppId, Guid settingId, CancellationToken ct = default)
    {
        var setting = await db.TenantAppSettings
            .FirstOrDefaultAsync(s => s.Id == settingId && s.TenantAppId == tenantAppId && s.TenantApp.TenantId == tenantId, ct);

        if (setting is null) return OperationResult.NotFound("La variable no existe.");

        if (setting.IsReadOnly)
            return OperationResult.Forbidden("La variable la gestiona la plataforma y no se puede eliminar.");

        db.TenantAppSettings.Remove(setting);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("setting.deleted", nameof(TenantAppSetting), settingId.ToString(), tenantId,
            new { setting.Key }, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<string?>> RevealAsync(Guid tenantId, Guid tenantAppId, Guid settingId, CancellationToken ct = default)
    {
        var setting = await db.TenantAppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == settingId && s.TenantAppId == tenantAppId && s.TenantApp.TenantId == tenantId, ct);

        if (setting is null) return OperationResult<string?>.NotFound("La variable no existe.");

        await audit.LogAsync("setting.revealed", nameof(TenantAppSetting), settingId.ToString(), tenantId,
            new { setting.Key }, ct: ct);

        return OperationResult<string?>.Ok(Decrypt(setting, reveal: true));
    }

    /// <summary>Valida y aplica un valor sobre el grafo cargado, sin guardar todavía.</summary>
    private async Task<OperationResult<TenantAppSettingDto>> UpsertCoreAsync(
        TenantApp subscription, UpsertSettingRequest request, CancellationToken ct)
    {
        var key = request.Key.Trim();
        var definition = subscription.App.SettingDefinitions
            .FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

        // El esquema de la app manda sobre lo que envíe el cliente.
        var isSecret = definition?.IsSecret ?? request.IsSecret;
        var dataType = definition?.DataType ?? Mappings.ResolveDataType(request.DataType, isSecret);

        var validation = Validate(request.Value, dataType, definition);
        if (validation is not null)
            return OperationResult<TenantAppSettingDto>.Invalid(validation, new Dictionary<string, string[]> { [key] = [validation] });

        var existing = await db.TenantAppSettings
            .FirstOrDefaultAsync(s => s.TenantAppId == subscription.Id && s.Key == key && s.Environment == request.Environment, ct);

        var hasIncomingValue = !string.IsNullOrEmpty(request.Value) && request.Value != Masking.Placeholder;

        if (existing is null)
        {
            existing = new TenantAppSetting
            {
                TenantAppId = subscription.Id,
                SettingDefinitionId = definition?.Id,
                Key = key,
                Environment = request.Environment,
                CreatedBy = currentUser.UserId
            };
            db.TenantAppSettings.Add(existing);
        }
        else
        {
            if (existing.IsReadOnly)
                return OperationResult<TenantAppSettingDto>.Forbidden($"La variable {key} la gestiona la plataforma.");

            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = currentUser.UserId;
        }

        existing.SettingDefinitionId = definition?.Id ?? existing.SettingDefinitionId;
        existing.DataType = dataType;
        existing.IsSecret = isSecret;
        existing.Description = request.Description?.Trim() ?? definition?.Description;

        // Recibir el valor enmascarado significa "no lo cambies": es lo que devuelve
        // el formulario cuando el usuario no tocó el campo secreto.
        if (hasIncomingValue)
        {
            existing.Value = isSecret ? protector.Protect(request.Value!) : request.Value;
            existing.IsEncrypted = isSecret;
        }
        else if (request.Value is null or "")
        {
            existing.Value = null;
            existing.IsEncrypted = false;
        }

        return OperationResult<TenantAppSettingDto>.Ok(existing.ToDto(Decrypt(existing, reveal: false), reveal: false));
    }

    private static string? Validate(string? value, SettingDataType dataType, AppSettingDefinition? definition)
    {
        // El placeholder significa "el usuario no tocó este campo secreto": no se valida ni se pisa.
        if (value == Masking.Placeholder) return null;

        if (string.IsNullOrEmpty(value))
            return definition is { IsRequired: true } && string.IsNullOrEmpty(definition.DefaultValue)
                ? $"La variable {definition.Key} es obligatoria."
                : null;

        switch (dataType)
        {
            case SettingDataType.Number when !decimal.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _):
                return "El valor debe ser numérico.";
            case SettingDataType.Boolean when !bool.TryParse(value, out _):
                return "El valor debe ser true o false.";
            case SettingDataType.Url when !Uri.TryCreate(value, UriKind.Absolute, out _):
                return "El valor debe ser una URL absoluta.";
            case SettingDataType.Email when !value.Contains('@') || value.StartsWith('@') || value.EndsWith('@'):
                return "El valor debe ser un correo válido.";
            case SettingDataType.Json:
                try { using var _ = JsonDocument.Parse(value); }
                catch (JsonException) { return "El valor debe ser JSON válido."; }
                break;
        }

        if (!string.IsNullOrWhiteSpace(definition?.ValidationRegex))
        {
            try
            {
                if (!Regex.IsMatch(value, definition.ValidationRegex, RegexOptions.None, RegexTimeout))
                    return $"El valor no cumple el formato esperado para {definition.Key}.";
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                // Una expresión mal formada en el catálogo no debe bloquear al cliente.
            }
        }

        if (!string.IsNullOrWhiteSpace(definition?.AllowedValues))
        {
            try
            {
                var allowed = JsonSerializer.Deserialize<string[]>(definition.AllowedValues);
                if (allowed is { Length: > 0 } && !allowed.Contains(value, StringComparer.Ordinal))
                    return $"El valor debe ser uno de: {string.Join(", ", allowed)}.";
            }
            catch (JsonException)
            {
                // Catálogo mal formado: se ignora la restricción en lugar de rechazar el guardado.
            }
        }

        return null;
    }

    private string? Decrypt(TenantAppSetting setting, bool reveal)
    {
        if (!setting.IsEncrypted || string.IsNullOrEmpty(setting.Value)) return setting.Value;
        if (!reveal) return setting.Value;

        return protector.TryUnprotect(setting.Value, out var plain) ? plain : null;
    }

    private Task<bool> SubscriptionExistsAsync(Guid tenantId, Guid tenantAppId, CancellationToken ct) =>
        db.TenantApps.AnyAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct);
}
