using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Application.Dtos;

public sealed record UserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? AvatarUrl,
    string? JobTitle,
    string? PhoneNumber,
    bool IsActive,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    bool MustChangePassword,
    bool IsLockedOut,
    string TimeZone,
    string Locale,
    IReadOnlyList<string> Roles,
    IReadOnlyList<MembershipDto> Memberships,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string FullName,
    string? AvatarUrl,
    string? JobTitle,
    bool IsActive,
    bool IsLockedOut,
    IReadOnlyList<string> Roles,
    int TenantCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    /// <summary>Si se omite, se genera una contraseña temporal y se devuelve una sola vez.</summary>
    [MinLength(10), MaxLength(128)]
    public string? Password { get; init; }

    [MaxLength(150)] public string? JobTitle { get; init; }
    [Phone, MaxLength(40)] public string? PhoneNumber { get; init; }
    [MaxLength(500)] public string? AvatarUrl { get; init; }
    [MaxLength(60)] public string? TimeZone { get; init; }
    [MaxLength(20)] public string? Locale { get; init; }

    public bool IsActive { get; init; } = true;
    public bool MustChangePassword { get; init; } = true;

    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Empresas a las que se añade el usuario al crearlo.</summary>
    public IReadOnlyList<UserMembershipRequest> Memberships { get; init; } = [];
}

public sealed record UserMembershipRequest
{
    [Required]
    public Guid TenantId { get; init; }

    public TenantRole Role { get; init; } = TenantRole.Member;

    public bool IsDefault { get; init; }
}

public sealed record UpdateUserRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    [MaxLength(150)] public string? JobTitle { get; init; }
    [Phone, MaxLength(40)] public string? PhoneNumber { get; init; }
    [MaxLength(500)] public string? AvatarUrl { get; init; }
    [MaxLength(60)] public string? TimeZone { get; init; }
    [MaxLength(20)] public string? Locale { get; init; }

    public bool IsActive { get; init; } = true;

    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record ResetPasswordRequest
{
    /// <summary>Si se omite, la plataforma genera una contraseña temporal.</summary>
    [MinLength(10), MaxLength(128)]
    public string? NewPassword { get; init; }

    public bool MustChangePassword { get; init; } = true;
}

public sealed record TemporaryPasswordDto(Guid UserId, string Email, string? TemporaryPassword);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, int UserCount);
