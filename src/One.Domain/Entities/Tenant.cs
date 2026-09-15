using One.Domain.Common;
using One.Domain.Enums;

namespace One.Domain.Entities;

/// <summary>Empresa cliente. Es la unidad de aislamiento de toda la configuración.</summary>
public class Tenant : SoftDeletableEntity
{
    /// <summary>Nombre comercial.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Identificador legible y único, usado en URLs y en el header X-Tenant.</summary>
    public string Slug { get; set; } = string.Empty;

    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }

    /// <summary>Color de acento para personalizar el portal de la empresa.</summary>
    public string? BrandColor { get; set; }

    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public string? Plan { get; set; }
    public string? Notes { get; set; }

    /// <summary>Tope de apps asignables. Null = sin límite.</summary>
    public int? MaxApps { get; set; }

    /// <summary>Tope de usuarios miembros. Null = sin límite.</summary>
    public int? MaxUsers { get; set; }

    public ICollection<TenantUser> Members { get; set; } = [];
    public ICollection<TenantApp> Apps { get; set; } = [];
}
