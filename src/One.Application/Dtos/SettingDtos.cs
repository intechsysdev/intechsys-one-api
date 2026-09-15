using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Application.Dtos;

/// <summary>
/// Valor de una variable tal como lo ve el portal. Si es secreta, <see cref="Value"/>
/// viaja enmascarado salvo que se pida explícitamente revelarlo.
/// </summary>
public sealed record TenantAppSettingDto(
    Guid Id,
    Guid TenantAppId,
    string Key,
    string? Value,
    bool IsSecret,
    bool HasValue,
    SettingDataType DataType,
    AppEnvironment Environment,
    string? Description,
    bool IsReadOnly,
    bool IsFromSchema,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertSettingRequest
{
    [Required, MaxLength(120), RegularExpression("^[A-Za-z_][A-Za-z0-9_.:-]*$", ErrorMessage = "La clave debe empezar por letra y solo admite letras, números, punto, guion, guion bajo y dos puntos.")]
    public string Key { get; init; } = string.Empty;

    [MaxLength(8000)] public string? Value { get; init; }

    public SettingDataType DataType { get; init; } = SettingDataType.String;

    public bool IsSecret { get; init; }

    public AppEnvironment Environment { get; init; } = AppEnvironment.Production;

    [MaxLength(500)] public string? Description { get; init; }
}

public sealed record BulkUpsertSettingsRequest
{
    public AppEnvironment Environment { get; init; } = AppEnvironment.Production;

    [Required]
    public IReadOnlyList<UpsertSettingRequest> Settings { get; init; } = [];
}
