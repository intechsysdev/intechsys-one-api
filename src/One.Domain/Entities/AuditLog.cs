namespace One.Domain.Entities;

/// <summary>Traza de toda operación sensible del portal y de la API de integración.</summary>
public class AuditLog
{
    public long Id { get; set; }

    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>Nombre o email del actor en el momento del hecho, congelado para la traza.</summary>
    public string? ActorName { get; set; }

    /// <summary>Verbo de la acción, p. ej. "credential.rotated".</summary>
    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>Detalle en JSON. Nunca contiene secretos en claro.</summary>
    public string? Metadata { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
