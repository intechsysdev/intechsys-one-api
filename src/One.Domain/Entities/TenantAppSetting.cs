using One.Domain.Common;
using One.Domain.Enums;

namespace One.Domain.Entities;

/// <summary>Valor concreto de una variable de configuración para una empresa, app y entorno.</summary>
public class TenantAppSetting : AuditableEntity
{
    public Guid TenantAppId { get; set; }
    public TenantApp TenantApp { get; set; } = null!;

    /// <summary>Definición de catálogo asociada. Null si es una variable libre creada por el usuario.</summary>
    public Guid? SettingDefinitionId { get; set; }
    public AppSettingDefinition? SettingDefinition { get; set; }

    public string Key { get; set; } = string.Empty;

    /// <summary>Valor en claro cuando no es secreto; cifrado AES-GCM cuando lo es.</summary>
    public string? Value { get; set; }

    public SettingDataType DataType { get; set; } = SettingDataType.String;

    public bool IsSecret { get; set; }

    /// <summary>Marca si <see cref="Value"/> está cifrado en reposo.</summary>
    public bool IsEncrypted { get; set; }

    public AppEnvironment Environment { get; set; } = AppEnvironment.Production;

    public string? Description { get; set; }

    /// <summary>Las variables de solo lectura las gestiona la plataforma, no el cliente.</summary>
    public bool IsReadOnly { get; set; }
}
