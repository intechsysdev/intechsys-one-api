namespace One.Application.Dtos;

public sealed record DashboardStatsDto(
    int TotalTenants,
    int ActiveTenants,
    int TotalApps,
    int ActiveApps,
    int TotalUsers,
    int ActiveUsers,
    int TotalSubscriptions,
    int ActiveCredentials,
    int ExpiringCredentials,
    int RevokedCredentials,
    IReadOnlyList<TenantAdoptionDto> TopTenants,
    IReadOnlyList<AppAdoptionDto> TopApps,
    IReadOnlyList<AuditLogDto> RecentActivity);

public sealed record TenantAdoptionDto(Guid TenantId, string Name, string Slug, string? LogoUrl, string? BrandColor, int AppCount, int UserCount);

public sealed record AppAdoptionDto(Guid AppId, string Name, string Slug, string? IconUrl, string? Color, int TenantCount, int CredentialCount);

public sealed record AuditLogDto(
    long Id,
    Guid? TenantId,
    string? TenantName,
    Guid? UserId,
    string? ActorName,
    string Action,
    string? EntityType,
    string? EntityId,
    string? Metadata,
    string? IpAddress,
    bool Success,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);
