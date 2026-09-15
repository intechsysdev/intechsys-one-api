using One.Application.Dtos;
using One.Domain.Entities;
using One.Domain.Enums;

namespace One.Infrastructure.Services.Internal;

/// <summary>Conversión de entidades a DTO. Centralizada para que el enmascarado sea consistente.</summary>
internal static class Mappings
{
    public static TenantDto ToDto(this Tenant t, int userCount, int appCount, int credentialCount) => new(
        t.Id, t.Name, t.Slug, t.LegalName, t.TaxId, t.ContactEmail, t.ContactPhone, t.Website,
        t.LogoUrl, t.BrandColor, t.Country, t.City, t.Address, t.Status, t.Plan, t.Notes,
        t.MaxApps, t.MaxUsers, userCount, appCount, credentialCount, t.CreatedAt, t.UpdatedAt);

    public static AppDto ToDto(this AppDefinition a, int settingCount, int tenantCount) => new(
        a.Id, a.Name, a.Slug, a.Description, a.Category, a.IconUrl, a.Color, a.Version,
        a.HomepageUrl, a.DocumentationUrl, a.SupportEmail, a.IsActive, a.IsPublic,
        a.AvailableScopes, settingCount, tenantCount, a.CreatedAt, a.UpdatedAt);

    public static AppSettingDefinitionDto ToDto(this AppSettingDefinition d) => new(
        d.Id, d.AppId, d.Key, d.Label, d.Description, d.Placeholder, d.DataType, d.IsRequired,
        d.IsSecret, d.DefaultValue, d.ValidationRegex, d.AllowedValues, d.Group, d.DisplayOrder);

    public static ApiCredentialDto ToDto(this ApiCredential c) => new(
        c.Id, c.TenantAppId, c.Name, c.Environment, c.ClientId, c.KeyPrefix,
        Masking.ApiKey(c.KeyPrefix), Masking.Secret(c.SecretLast4), c.Scopes, c.AllowedIps,
        c.Status, c.ExpiresAt, c.LastUsedAt, c.LastUsedIp, c.UsageCount,
        c.RevokedAt, c.RevokedReason, c.CreatedAt);

    public static TenantAppSettingDto ToDto(this TenantAppSetting s, string? resolvedValue, bool reveal) => new(
        s.Id, s.TenantAppId, s.Key,
        Masking.SettingValue(resolvedValue, s.IsSecret, reveal),
        s.IsSecret,
        !string.IsNullOrEmpty(s.Value),
        s.DataType, s.Environment, s.Description, s.IsReadOnly,
        s.SettingDefinitionId is not null,
        s.CreatedAt, s.UpdatedAt);

    public static TenantAppDto ToDto(
        this TenantApp s,
        int credentialCount,
        int activeCredentialCount,
        int settingCount,
        int missingRequiredSettings) => new(
        s.Id, s.TenantId, s.Tenant.Name, s.Tenant.Slug,
        s.AppId, s.App.Name, s.App.Slug, s.App.IconUrl, s.App.Color, s.App.Category,
        s.DisplayName, s.IsEnabled, s.Status, s.SubscribedAt, s.ExpiresAt,
        s.GrantedScopes, s.AllowedOrigins, s.WebhookUrl, s.Notes,
        credentialCount, activeCredentialCount, settingCount, missingRequiredSettings);

    public static TenantMemberDto ToDto(this TenantUser m) => new(
        m.Id, m.UserId, m.User.Email ?? string.Empty, m.User.FirstName, m.User.LastName,
        m.User.FullName, m.User.AvatarUrl, m.User.JobTitle, m.Role, m.IsActive, m.IsDefault,
        m.User.IsActive, m.JoinedAt, m.User.LastLoginAt);

    public static MembershipDto ToMembershipDto(this TenantUser m) => new(
        m.TenantId, m.Tenant.Name, m.Tenant.Slug, m.Tenant.LogoUrl, m.Role, m.IsDefault, m.Tenant.Status);

    public static AuditLogDto ToDto(this AuditLog l, string? tenantName) => new(
        l.Id, l.TenantId, tenantName, l.UserId, l.ActorName, l.Action, l.EntityType,
        l.EntityId, l.Metadata, l.IpAddress, l.Success, l.ErrorMessage, l.CreatedAt);

    public static IReadOnlyList<string> SplitScopes(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([' ', ',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IReadOnlyList<string> SplitLines(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static SettingDataType ResolveDataType(SettingDataType requested, bool isSecret) =>
        isSecret && requested == SettingDataType.String ? SettingDataType.Secret : requested;
}
