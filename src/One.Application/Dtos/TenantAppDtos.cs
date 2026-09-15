using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Application.Dtos;

public sealed record TenantAppDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    Guid AppId,
    string AppName,
    string AppSlug,
    string? AppIconUrl,
    string? AppColor,
    string? AppCategory,
    string? DisplayName,
    bool IsEnabled,
    SubscriptionStatus Status,
    DateTimeOffset SubscribedAt,
    DateTimeOffset? ExpiresAt,
    string? GrantedScopes,
    string? AllowedOrigins,
    string? WebhookUrl,
    string? Notes,
    int CredentialCount,
    int ActiveCredentialCount,
    int SettingCount,
    int MissingRequiredSettings);

public sealed record TenantAppDetailDto(
    TenantAppDto Subscription,
    IReadOnlyList<AppSettingDefinitionDto> Schema,
    IReadOnlyList<TenantAppSettingDto> Settings,
    IReadOnlyList<ApiCredentialDto> Credentials);

public sealed record AssignAppRequest
{
    [Required]
    public Guid AppId { get; init; }

    [MaxLength(160)] public string? DisplayName { get; init; }

    public bool IsEnabled { get; init; } = true;

    public DateTimeOffset? ExpiresAt { get; init; }

    [MaxLength(1000)] public string? GrantedScopes { get; init; }
    [MaxLength(2000)] public string? AllowedOrigins { get; init; }
    [MaxLength(500)] public string? WebhookUrl { get; init; }
    [MaxLength(2000)] public string? Notes { get; init; }

    /// <summary>Crea las variables del esquema con sus valores por defecto.</summary>
    public bool SeedDefaultSettings { get; init; } = true;

    /// <summary>Emite una credencial inicial para el entorno indicado.</summary>
    public AppEnvironment? CreateCredentialForEnvironment { get; init; }
}

public sealed record UpdateTenantAppRequest
{
    [MaxLength(160)] public string? DisplayName { get; init; }

    public bool IsEnabled { get; init; } = true;

    public SubscriptionStatus Status { get; init; } = SubscriptionStatus.Active;

    public DateTimeOffset? ExpiresAt { get; init; }

    [MaxLength(1000)] public string? GrantedScopes { get; init; }
    [MaxLength(2000)] public string? AllowedOrigins { get; init; }
    [MaxLength(500)] public string? WebhookUrl { get; init; }
    [MaxLength(2000)] public string? Notes { get; init; }
}
