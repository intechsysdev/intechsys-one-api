using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Application.Dtos;

public sealed record AppDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? Category,
    string? IconUrl,
    string? Color,
    string? Version,
    string? HomepageUrl,
    string? DocumentationUrl,
    string? SupportEmail,
    bool IsActive,
    bool IsPublic,
    string? AvailableScopes,
    int SettingCount,
    int TenantCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record AppDetailDto(
    AppDto App,
    IReadOnlyList<AppSettingDefinitionDto> SettingDefinitions);

public sealed record CreateAppRequest
{
    [Required, MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(80), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "El slug solo admite minúsculas, números y guiones.")]
    public string? Slug { get; init; }

    [MaxLength(1000)] public string? Description { get; init; }
    [MaxLength(80)] public string? Category { get; init; }
    [MaxLength(500)] public string? IconUrl { get; init; }
    [MaxLength(20)] public string? Color { get; init; }
    [MaxLength(40)] public string? Version { get; init; }
    [MaxLength(300)] public string? HomepageUrl { get; init; }
    [MaxLength(300)] public string? DocumentationUrl { get; init; }
    [EmailAddress, MaxLength(256)] public string? SupportEmail { get; init; }

    public bool IsActive { get; init; } = true;
    public bool IsPublic { get; init; } = true;

    [MaxLength(1000)] public string? AvailableScopes { get; init; }

    /// <summary>Variables que la app espera. Se pueden declarar en el alta.</summary>
    public IReadOnlyList<UpsertSettingDefinitionRequest> SettingDefinitions { get; init; } = [];
}

public sealed record UpdateAppRequest
{
    [Required, MaxLength(160)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)] public string? Description { get; init; }
    [MaxLength(80)] public string? Category { get; init; }
    [MaxLength(500)] public string? IconUrl { get; init; }
    [MaxLength(20)] public string? Color { get; init; }
    [MaxLength(40)] public string? Version { get; init; }
    [MaxLength(300)] public string? HomepageUrl { get; init; }
    [MaxLength(300)] public string? DocumentationUrl { get; init; }
    [EmailAddress, MaxLength(256)] public string? SupportEmail { get; init; }

    public bool IsActive { get; init; } = true;
    public bool IsPublic { get; init; } = true;

    [MaxLength(1000)] public string? AvailableScopes { get; init; }
}

public sealed record AppSettingDefinitionDto(
    Guid Id,
    Guid AppId,
    string Key,
    string Label,
    string? Description,
    string? Placeholder,
    SettingDataType DataType,
    bool IsRequired,
    bool IsSecret,
    string? DefaultValue,
    string? ValidationRegex,
    string? AllowedValues,
    string? Group,
    int DisplayOrder);

public sealed record UpsertSettingDefinitionRequest
{
    public Guid? Id { get; init; }

    [Required, MaxLength(120), RegularExpression("^[A-Za-z_][A-Za-z0-9_.:-]*$", ErrorMessage = "La clave debe empezar por letra y solo admite letras, números, punto, guion, guion bajo y dos puntos.")]
    public string Key { get; init; } = string.Empty;

    [Required, MaxLength(160)]
    public string Label { get; init; } = string.Empty;

    [MaxLength(1000)] public string? Description { get; init; }
    [MaxLength(200)] public string? Placeholder { get; init; }

    public SettingDataType DataType { get; init; } = SettingDataType.String;

    public bool IsRequired { get; init; }
    public bool IsSecret { get; init; }

    [MaxLength(2000)] public string? DefaultValue { get; init; }
    [MaxLength(500)] public string? ValidationRegex { get; init; }
    [MaxLength(2000)] public string? AllowedValues { get; init; }
    [MaxLength(80)] public string? Group { get; init; }

    public int DisplayOrder { get; init; }
}
