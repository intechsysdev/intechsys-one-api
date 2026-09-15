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
/// Ciclo de vida de las api keys de integración. El par en claro se devuelve una única vez,
/// al crear o al rotar: a partir de ahí solo queda el hash.
/// </summary>
public sealed class CredentialService(
    OneDbContext db,
    ICredentialGenerator generator,
    IAuditService audit,
    ICurrentUser currentUser) : ICredentialService
{
    private const string RevealWarning =
        "Guarde estos valores ahora: el secreto no se puede volver a consultar. Si lo pierde, rote la credencial.";

    public async Task<OperationResult<IReadOnlyList<ApiCredentialDto>>> ListAsync(Guid tenantId, Guid tenantAppId, CancellationToken ct = default)
    {
        if (!await db.TenantApps.AnyAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct))
            return OperationResult<IReadOnlyList<ApiCredentialDto>>.NotFound("La app no está asignada a esta empresa.");

        var items = await db.ApiCredentials.AsNoTracking()
            .Where(c => c.TenantAppId == tenantAppId)
            .OrderBy(c => c.Status).ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return OperationResult<IReadOnlyList<ApiCredentialDto>>.Ok([.. items.Select(c => c.ToDto())]);
    }

    public async Task<OperationResult<ApiCredentialSecretDto>> CreateAsync(
        Guid tenantId, Guid tenantAppId, CreateCredentialRequest request, CancellationToken ct = default)
    {
        var subscription = await db.TenantApps.FirstOrDefaultAsync(s => s.Id == tenantAppId && s.TenantId == tenantId, ct);
        if (subscription is null)
            return OperationResult<ApiCredentialSecretDto>.NotFound("La app no está asignada a esta empresa.");

        if (request.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            return OperationResult<ApiCredentialSecretDto>.Invalid("La fecha de caducidad debe ser futura.");

        var generated = generator.Generate(request.Environment);

        var credential = new ApiCredential
        {
            TenantAppId = tenantAppId,
            Name = request.Name.Trim(),
            Environment = request.Environment,
            ClientId = generated.ClientId,
            KeyPrefix = generated.KeyPrefix,
            ApiKeyHash = generated.ApiKeyHash,
            SecretHash = generated.SecretHash,
            SecretLast4 = generated.SecretLast4,
            Scopes = request.Scopes?.Trim() ?? subscription.GrantedScopes,
            AllowedIps = request.AllowedIps?.Trim(),
            ExpiresAt = request.ExpiresAt,
            CreatedBy = currentUser.UserId
        };

        db.ApiCredentials.Add(credential);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("credential.created", nameof(ApiCredential), credential.Id.ToString(), tenantId,
            new { credential.Name, Environment = credential.Environment.ToString(), credential.ClientId }, ct: ct);

        return OperationResult<ApiCredentialSecretDto>.Ok(
            new ApiCredentialSecretDto(credential.ToDto(), generated.ApiKey, generated.ApiSecret, RevealWarning));
    }

    public async Task<OperationResult<ApiCredentialDto>> UpdateAsync(
        Guid tenantId, Guid tenantAppId, Guid credentialId, UpdateCredentialRequest request, CancellationToken ct = default)
    {
        var credential = await LoadAsync(tenantId, tenantAppId, credentialId, ct);
        if (credential is null) return OperationResult<ApiCredentialDto>.NotFound("La credencial no existe.");

        credential.Name = request.Name.Trim();
        credential.Scopes = request.Scopes?.Trim();
        credential.AllowedIps = request.AllowedIps?.Trim();
        credential.ExpiresAt = request.ExpiresAt;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        credential.UpdatedBy = currentUser.UserId;

        // Volver a poner una caducidad futura reactiva una credencial que había expirado.
        if (credential.Status == CredentialStatus.Expired && (request.ExpiresAt is null || request.ExpiresAt > DateTimeOffset.UtcNow))
            credential.Status = CredentialStatus.Active;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("credential.updated", nameof(ApiCredential), credentialId.ToString(), tenantId,
            new { credential.Name }, ct: ct);

        return OperationResult<ApiCredentialDto>.Ok(credential.ToDto());
    }

    public async Task<OperationResult<ApiCredentialSecretDto>> RotateAsync(
        Guid tenantId, Guid tenantAppId, Guid credentialId, CancellationToken ct = default)
    {
        var credential = await LoadAsync(tenantId, tenantAppId, credentialId, ct);
        if (credential is null) return OperationResult<ApiCredentialSecretDto>.NotFound("La credencial no existe.");

        if (credential.Status == CredentialStatus.Revoked)
            return OperationResult<ApiCredentialSecretDto>.Conflict("No se puede rotar una credencial revocada. Cree una nueva.");

        var generated = generator.Generate(credential.Environment);

        credential.ClientId = generated.ClientId;
        credential.KeyPrefix = generated.KeyPrefix;
        credential.ApiKeyHash = generated.ApiKeyHash;
        credential.SecretHash = generated.SecretHash;
        credential.SecretLast4 = generated.SecretLast4;
        credential.Status = CredentialStatus.Active;
        credential.UsageCount = 0;
        credential.LastUsedAt = null;
        credential.LastUsedIp = null;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        credential.UpdatedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("credential.rotated", nameof(ApiCredential), credentialId.ToString(), tenantId,
            new { credential.Name, credential.ClientId }, ct: ct);

        return OperationResult<ApiCredentialSecretDto>.Ok(
            new ApiCredentialSecretDto(credential.ToDto(), generated.ApiKey, generated.ApiSecret, RevealWarning));
    }

    public async Task<OperationResult> RevokeAsync(
        Guid tenantId, Guid tenantAppId, Guid credentialId, RevokeCredentialRequest request, CancellationToken ct = default)
    {
        var credential = await LoadAsync(tenantId, tenantAppId, credentialId, ct);
        if (credential is null) return OperationResult.NotFound("La credencial no existe.");

        if (credential.Status == CredentialStatus.Revoked) return OperationResult.Ok();

        credential.Status = CredentialStatus.Revoked;
        credential.RevokedAt = DateTimeOffset.UtcNow;
        credential.RevokedBy = currentUser.UserId;
        credential.RevokedReason = request.Reason?.Trim();

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("credential.revoked", nameof(ApiCredential), credentialId.ToString(), tenantId,
            new { credential.Name, credential.RevokedReason }, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteAsync(Guid tenantId, Guid tenantAppId, Guid credentialId, CancellationToken ct = default)
    {
        var credential = await LoadAsync(tenantId, tenantAppId, credentialId, ct);
        if (credential is null) return OperationResult.NotFound("La credencial no existe.");

        db.ApiCredentials.Remove(credential);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("credential.deleted", nameof(ApiCredential), credentialId.ToString(), tenantId,
            new { credential.Name, credential.ClientId }, ct: ct);

        return OperationResult.Ok();
    }

    private Task<ApiCredential?> LoadAsync(Guid tenantId, Guid tenantAppId, Guid credentialId, CancellationToken ct) =>
        db.ApiCredentials.FirstOrDefaultAsync(
            c => c.Id == credentialId && c.TenantAppId == tenantAppId && c.TenantApp.TenantId == tenantId, ct);
}
