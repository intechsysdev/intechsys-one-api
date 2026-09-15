using One.Domain.Enums;

namespace One.Application.Dtos;

/// <summary>Empresa tal como la ve la app integrada.</summary>
public sealed record IntegrationTenantDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    string? BrandColor,
    string? Country,
    string? ContactEmail,
    TenantStatus Status);

/// <summary>App tal como la ve la propia app integrada.</summary>
public sealed record IntegrationAppDto(
    Guid Id,
    string Name,
    string Slug,
    string? DisplayName,
    string? Version,
    string? Category);

/// <summary>
/// Payload que recibe una app integrada al autenticarse con su api key y secreto.
/// Contiene su identidad, la de la empresa y todas sus variables ya resueltas.
/// </summary>
public sealed record AppConfigurationDto(
    IntegrationTenantDto Tenant,
    IntegrationAppDto App,
    AppEnvironment Environment,
    string ClientId,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> AllowedOrigins,
    IReadOnlyDictionary<string, string?> Settings,
    DateTimeOffset? SubscriptionExpiresAt,
    DateTimeOffset RetrievedAt,
    string ConfigVersion);

/// <summary>Respuesta de /integration/verify: comprobación ligera de credenciales.</summary>
public sealed record CredentialIntrospectionDto(
    bool Active,
    Guid CredentialId,
    string ClientId,
    Guid TenantId,
    string TenantSlug,
    Guid AppId,
    string AppSlug,
    AppEnvironment Environment,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt);
