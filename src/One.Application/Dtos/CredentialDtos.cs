using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Application.Dtos;

public sealed record ApiCredentialDto(
    Guid Id,
    Guid TenantAppId,
    string Name,
    AppEnvironment Environment,
    string ClientId,
    string KeyPrefix,
    string MaskedApiKey,
    string MaskedSecret,
    string? Scopes,
    string? AllowedIps,
    CredentialStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    string? LastUsedIp,
    long UsageCount,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    DateTimeOffset CreatedAt);

/// <summary>
/// Respuesta de alta o rotación. Es la única vez que la api key y el secreto
/// viajan en claro: después solo queda su hash en la base.
/// </summary>
public sealed record ApiCredentialSecretDto(
    ApiCredentialDto Credential,
    string ApiKey,
    string ApiSecret,
    string Warning);

public sealed record CreateCredentialRequest
{
    [Required, MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    public AppEnvironment Environment { get; init; } = AppEnvironment.Development;

    [MaxLength(1000)] public string? Scopes { get; init; }
    [MaxLength(2000)] public string? AllowedIps { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record UpdateCredentialRequest
{
    [Required, MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)] public string? Scopes { get; init; }
    [MaxLength(2000)] public string? AllowedIps { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record RevokeCredentialRequest
{
    [MaxLength(500)] public string? Reason { get; init; }
}
