using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Application.Dtos;

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    string? LegalName,
    string? TaxId,
    string? ContactEmail,
    string? ContactPhone,
    string? Website,
    string? LogoUrl,
    string? BrandColor,
    string? Country,
    string? City,
    string? Address,
    TenantStatus Status,
    string? Plan,
    string? Notes,
    int? MaxApps,
    int? MaxUsers,
    int UserCount,
    int AppCount,
    int CredentialCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record TenantListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    string? BrandColor,
    TenantStatus Status,
    string? Plan,
    string? ContactEmail,
    int UserCount,
    int AppCount,
    DateTimeOffset CreatedAt);

public sealed record CreateTenantRequest
{
    [Required, MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Si se omite se deriva del nombre.</summary>
    [MaxLength(80), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "El slug solo admite minúsculas, números y guiones.")]
    public string? Slug { get; init; }

    [MaxLength(200)] public string? LegalName { get; init; }
    [MaxLength(50)] public string? TaxId { get; init; }
    [EmailAddress, MaxLength(256)] public string? ContactEmail { get; init; }
    [MaxLength(50)] public string? ContactPhone { get; init; }
    [MaxLength(300)] public string? Website { get; init; }
    [MaxLength(500)] public string? LogoUrl { get; init; }
    [MaxLength(20)] public string? BrandColor { get; init; }
    [MaxLength(100)] public string? Country { get; init; }
    [MaxLength(100)] public string? City { get; init; }
    [MaxLength(300)] public string? Address { get; init; }

    public TenantStatus Status { get; init; } = TenantStatus.Trial;

    [MaxLength(60)] public string? Plan { get; init; }
    [MaxLength(2000)] public string? Notes { get; init; }

    [Range(1, 10_000)] public int? MaxApps { get; init; }
    [Range(1, 100_000)] public int? MaxUsers { get; init; }

    /// <summary>Usuario que queda como Owner de la empresa recién creada.</summary>
    public Guid? OwnerUserId { get; init; }
}

public sealed record UpdateTenantRequest
{
    [Required, MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(200)] public string? LegalName { get; init; }
    [MaxLength(50)] public string? TaxId { get; init; }
    [EmailAddress, MaxLength(256)] public string? ContactEmail { get; init; }
    [MaxLength(50)] public string? ContactPhone { get; init; }
    [MaxLength(300)] public string? Website { get; init; }
    [MaxLength(500)] public string? LogoUrl { get; init; }
    [MaxLength(20)] public string? BrandColor { get; init; }
    [MaxLength(100)] public string? Country { get; init; }
    [MaxLength(100)] public string? City { get; init; }
    [MaxLength(300)] public string? Address { get; init; }

    public TenantStatus Status { get; init; } = TenantStatus.Active;

    [MaxLength(60)] public string? Plan { get; init; }
    [MaxLength(2000)] public string? Notes { get; init; }

    [Range(1, 10_000)] public int? MaxApps { get; init; }
    [Range(1, 100_000)] public int? MaxUsers { get; init; }
}

public sealed record TenantMemberDto(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? AvatarUrl,
    string? JobTitle,
    TenantRole Role,
    bool IsActive,
    bool IsDefault,
    bool UserIsActive,
    DateTimeOffset? JoinedAt,
    DateTimeOffset? LastLoginAt);

public sealed record AddTenantMemberRequest
{
    [Required]
    public Guid UserId { get; init; }

    public TenantRole Role { get; init; } = TenantRole.Member;

    public bool IsDefault { get; init; }
}

public sealed record UpdateTenantMemberRequest
{
    public TenantRole Role { get; init; } = TenantRole.Member;
    public bool IsActive { get; init; } = true;
    public bool IsDefault { get; init; }
}
