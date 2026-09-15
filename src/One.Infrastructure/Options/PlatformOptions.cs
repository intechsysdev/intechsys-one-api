using System.ComponentModel.DataAnnotations;

namespace One.Infrastructure.Options;

/// <summary>Parámetros de emisión y validación del JWT del portal.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = "one-api";
    [Required] public string Audience { get; set; } = "one-front";

    /// <summary>Clave HMAC en base64 o texto plano. Mínimo 32 bytes.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 60;
    [Range(1, 365)] public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>Material criptográfico para proteger secretos en reposo y firmar api keys.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Clave AES-256 en base64 (32 bytes) con la que se cifran las variables secretas.</summary>
    [Required]
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>Pepper del HMAC con el que se derivan los hashes de api key y secreto.</summary>
    [Required, MinLength(32)]
    public string ApiKeyPepper { get; set; } = string.Empty;
}

/// <summary>Cuenta de administrador creada en el primer arranque.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; } = true;

    [EmailAddress] public string AdminEmail { get; set; } = "admin@intechsys.co";
    public string AdminPassword { get; set; } = "Admin!2026.One";
    public string AdminFirstName { get; set; } = "Administrador";
    public string AdminLastName { get; set; } = "Intechsys";

    /// <summary>Carga una empresa y una app de ejemplo para poder explorar el portal.</summary>
    public bool IncludeDemoData { get; set; } = true;
}
