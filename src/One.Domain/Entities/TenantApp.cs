using One.Domain.Common;
using One.Domain.Enums;

namespace One.Domain.Entities;

/// <summary>Asignación de una app del catálogo a una empresa. Es el contenedor de su configuración.</summary>
public class TenantApp : AuditableEntity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid AppId { get; set; }
    public AppDefinition App { get; set; } = null!;

    /// <summary>Alias opcional que la empresa da a la app.</summary>
    public string? DisplayName { get; set; }

    public bool IsEnabled { get; set; } = true;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTimeOffset SubscribedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Scopes concedidos a esta empresa para esta app, separados por espacio.</summary>
    public string? GrantedScopes { get; set; }

    /// <summary>Orígenes permitidos para CORS/redirecciones, uno por línea.</summary>
    public string? AllowedOrigins { get; set; }

    /// <summary>URL a la que la plataforma notifica cambios de configuración.</summary>
    public string? WebhookUrl { get; set; }

    public string? Notes { get; set; }

    public ICollection<ApiCredential> Credentials { get; set; } = [];
    public ICollection<TenantAppSetting> Settings { get; set; } = [];
}
