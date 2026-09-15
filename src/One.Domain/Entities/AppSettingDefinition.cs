using One.Domain.Common;
using One.Domain.Enums;

namespace One.Domain.Entities;

/// <summary>
/// Esquema de una variable de configuración que la app espera recibir.
/// Permite al portal renderizar el formulario y validar antes de guardar.
/// </summary>
public class AppSettingDefinition : AuditableEntity
{
    public Guid AppId { get; set; }
    public AppDefinition App { get; set; } = null!;

    /// <summary>Clave técnica que la app lee (p. ej. "SMTP_HOST").</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }

    public SettingDataType DataType { get; set; } = SettingDataType.String;

    public bool IsRequired { get; set; }

    /// <summary>Si el valor se cifra en reposo y nunca se devuelve en claro al portal.</summary>
    public bool IsSecret { get; set; }

    public string? DefaultValue { get; set; }

    /// <summary>Expresión regular opcional de validación.</summary>
    public string? ValidationRegex { get; set; }

    /// <summary>Opciones permitidas en formato JSON array, para renderizar un select.</summary>
    public string? AllowedValues { get; set; }

    /// <summary>Agrupador visual en el formulario (p. ej. "Conexión", "Notificaciones").</summary>
    public string? Group { get; set; }

    public int DisplayOrder { get; set; }
}
