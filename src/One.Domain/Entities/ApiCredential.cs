using One.Domain.Common;
using One.Domain.Enums;

namespace One.Domain.Entities;

/// <summary>
/// Par api key / secret con el que una app integrada se autentica para leer su configuración.
/// El secreto solo se muestra una vez: en la base únicamente queda su hash.
/// </summary>
public class ApiCredential : AuditableEntity
{
    public Guid TenantAppId { get; set; }
    public TenantApp TenantApp { get; set; } = null!;

    /// <summary>Nombre descriptivo dado por el usuario (p. ej. "Servidor de producción").</summary>
    public string Name { get; set; } = string.Empty;

    public AppEnvironment Environment { get; set; } = AppEnvironment.Development;

    /// <summary>Identificador público de cliente, seguro de mostrar y registrar en logs.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Prefijo visible de la api key, para poder reconocerla en el listado.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 de la api key completa.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 del secreto.</summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>Últimos 4 caracteres del secreto, como ayuda visual.</summary>
    public string SecretLast4 { get; set; } = string.Empty;

    public string? Scopes { get; set; }

    /// <summary>Lista blanca de IPs o CIDRs, una por línea. Vacío = sin restricción.</summary>
    public string? AllowedIps { get; set; }

    public CredentialStatus Status { get; set; } = CredentialStatus.Active;

    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public long UsageCount { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsUsable =>
        Status == CredentialStatus.Active &&
        (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}
