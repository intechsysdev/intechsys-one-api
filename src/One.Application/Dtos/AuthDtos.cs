using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Application.Dtos;

public sealed record LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

public sealed record RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    int ExpiresInSeconds,
    CurrentUserDto User);

public sealed record ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, MinLength(10)]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>Identidad y permisos efectivos del usuario autenticado.</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? AvatarUrl,
    string? JobTitle,
    bool IsPlatformAdmin,
    IReadOnlyList<string> Roles,
    IReadOnlyList<MembershipDto> Memberships);

/// <summary>Empresa a la que pertenece el usuario, con el rol que ostenta en ella.</summary>
public sealed record MembershipDto(
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    string? LogoUrl,
    TenantRole Role,
    bool IsDefault,
    TenantStatus TenantStatus);

public sealed record UpdateProfileRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    [MaxLength(150)]
    public string? JobTitle { get; init; }

    [MaxLength(500)]
    public string? AvatarUrl { get; init; }

    [MaxLength(60)]
    public string? TimeZone { get; init; }

    [MaxLength(20)]
    public string? Locale { get; init; }
}
