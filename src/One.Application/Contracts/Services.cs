using One.Application.Common;
using One.Application.Dtos;
using One.Domain.Enums;

namespace One.Application.Contracts;

public interface IAuthService
{
    Task<OperationResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<OperationResult<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task<OperationResult> LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task<OperationResult<CurrentUserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task<OperationResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<OperationResult<CurrentUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
}

public interface ITenantService
{
    /// <param name="restrictToTenantIds">Empresas visibles para el usuario. Null = sin restricción (plataforma).</param>
    Task<PagedResult<TenantListItemDto>> ListAsync(PageRequest page, TenantStatus? status, IReadOnlyCollection<Guid>? restrictToTenantIds, CancellationToken ct = default);
    Task<OperationResult<TenantDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<OperationResult<TenantDto>> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<OperationResult<TenantDto>> CreateAsync(CreateTenantRequest request, CancellationToken ct = default);
    Task<OperationResult<TenantDto>> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<OperationResult<IReadOnlyList<TenantMemberDto>>> ListMembersAsync(Guid tenantId, CancellationToken ct = default);
    Task<OperationResult<TenantMemberDto>> AddMemberAsync(Guid tenantId, AddTenantMemberRequest request, CancellationToken ct = default);
    Task<OperationResult<TenantMemberDto>> UpdateMemberAsync(Guid tenantId, Guid membershipId, UpdateTenantMemberRequest request, CancellationToken ct = default);
    Task<OperationResult> RemoveMemberAsync(Guid tenantId, Guid membershipId, CancellationToken ct = default);
}

public interface IAppService
{
    Task<PagedResult<AppDto>> ListAsync(PageRequest page, bool? isActive, string? category, CancellationToken ct = default);
    Task<OperationResult<AppDetailDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<OperationResult<AppDetailDto>> CreateAsync(CreateAppRequest request, CancellationToken ct = default);
    Task<OperationResult<AppDetailDto>> UpdateAsync(Guid id, UpdateAppRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<OperationResult<AppSettingDefinitionDto>> UpsertSettingDefinitionAsync(Guid appId, UpsertSettingDefinitionRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteSettingDefinitionAsync(Guid appId, Guid definitionId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct = default);
}

public interface ITenantAppService
{
    Task<OperationResult<IReadOnlyList<TenantAppDto>>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<OperationResult<TenantAppDetailDto>> GetAsync(Guid tenantId, Guid tenantAppId, bool revealSecrets, CancellationToken ct = default);
    Task<OperationResult<TenantAppDto>> AssignAsync(Guid tenantId, AssignAppRequest request, CancellationToken ct = default);
    Task<OperationResult<TenantAppDto>> UpdateAsync(Guid tenantId, Guid tenantAppId, UpdateTenantAppRequest request, CancellationToken ct = default);
    Task<OperationResult> RemoveAsync(Guid tenantId, Guid tenantAppId, CancellationToken ct = default);
}

public interface ISettingService
{
    Task<OperationResult<IReadOnlyList<TenantAppSettingDto>>> ListAsync(Guid tenantId, Guid tenantAppId, AppEnvironment? environment, bool reveal, CancellationToken ct = default);
    Task<OperationResult<TenantAppSettingDto>> UpsertAsync(Guid tenantId, Guid tenantAppId, UpsertSettingRequest request, CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<TenantAppSettingDto>>> BulkUpsertAsync(Guid tenantId, Guid tenantAppId, BulkUpsertSettingsRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(Guid tenantId, Guid tenantAppId, Guid settingId, CancellationToken ct = default);
    Task<OperationResult<string?>> RevealAsync(Guid tenantId, Guid tenantAppId, Guid settingId, CancellationToken ct = default);
}

public interface ICredentialService
{
    Task<OperationResult<IReadOnlyList<ApiCredentialDto>>> ListAsync(Guid tenantId, Guid tenantAppId, CancellationToken ct = default);
    Task<OperationResult<ApiCredentialSecretDto>> CreateAsync(Guid tenantId, Guid tenantAppId, CreateCredentialRequest request, CancellationToken ct = default);
    Task<OperationResult<ApiCredentialDto>> UpdateAsync(Guid tenantId, Guid tenantAppId, Guid credentialId, UpdateCredentialRequest request, CancellationToken ct = default);
    Task<OperationResult<ApiCredentialSecretDto>> RotateAsync(Guid tenantId, Guid tenantAppId, Guid credentialId, CancellationToken ct = default);
    Task<OperationResult> RevokeAsync(Guid tenantId, Guid tenantAppId, Guid credentialId, RevokeCredentialRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(Guid tenantId, Guid tenantAppId, Guid credentialId, CancellationToken ct = default);
}

public interface IUserService
{
    Task<PagedResult<UserListItemDto>> ListAsync(PageRequest page, Guid? tenantId, bool? isActive, string? role, CancellationToken ct = default);
    Task<OperationResult<UserDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<OperationResult<(UserDto User, string? TemporaryPassword)>> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<OperationResult<UserDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<OperationResult<TemporaryPasswordDto>> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
    Task<OperationResult> SetLockoutAsync(Guid id, bool locked, CancellationToken ct = default);
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default);
}

public interface IIntegrationService
{
    Task<OperationResult<AppConfigurationDto>> ResolveConfigurationAsync(string apiKey, string apiSecret, string? ipAddress, CancellationToken ct = default);
    Task<OperationResult<CredentialIntrospectionDto>> IntrospectAsync(string apiKey, string apiSecret, string? ipAddress, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(PageRequest page, Guid? tenantId, string? action, CancellationToken ct = default);
}
