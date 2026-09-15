using One.Domain.Common;

namespace One.Domain.Entities;

/// <summary>Catálogo global de aplicaciones integrables. Mapea a la tabla Apps.</summary>
public class AppDefinition : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Identificador técnico único de la app (p. ej. "pedidos-api").</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? IconUrl { get; set; }

    /// <summary>Color de acento para la tarjeta de la app en el portal.</summary>
    public string? Color { get; set; }

    public string? Version { get; set; }
    public string? HomepageUrl { get; set; }
    public string? DocumentationUrl { get; set; }
    public string? SupportEmail { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Si es visible en el catálogo para todas las empresas.</summary>
    public bool IsPublic { get; set; } = true;

    /// <summary>Scopes que la app puede solicitar, separados por espacio.</summary>
    public string? AvailableScopes { get; set; }

    public ICollection<AppSettingDefinition> SettingDefinitions { get; set; } = [];
    public ICollection<TenantApp> Subscriptions { get; set; } = [];
}
