using One.Domain.Enums;

namespace One.Application.Contracts;

/// <summary>Identidad del usuario que origina la petición HTTP en curso.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    IReadOnlyList<string> Roles { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}

/// <summary>Cifrado simétrico autenticado para los valores secretos guardados en la base.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
    bool TryUnprotect(string protectedValue, out string plaintext);
}

/// <summary>Par de credenciales recién generado, con los valores en claro todavía disponibles.</summary>
public sealed record GeneratedCredential(
    string ClientId,
    string ApiKey,
    string KeyPrefix,
    string ApiKeyHash,
    string ApiSecret,
    string SecretHash,
    string SecretLast4);

/// <summary>Genera y verifica api keys y secretos de integración.</summary>
public interface ICredentialGenerator
{
    GeneratedCredential Generate(AppEnvironment environment);
    string Hash(string value);
    bool Verify(string value, string hash);
}

/// <summary>Token de acceso emitido al portal.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, int ExpiresInSeconds);

/// <summary>Emisión de JWT y de tokens de refresco.</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(Guid userId, string email, string fullName, IEnumerable<string> roles, IEnumerable<(Guid TenantId, TenantRole Role)> memberships);
    string CreateRefreshToken();
    string HashRefreshToken(string token);
}

/// <summary>Escritura de la traza de auditoría.</summary>
public interface IAuditService
{
    /// <param name="actorName">Actor explícito. Necesario en el login, donde todavía no hay identidad en la petición.</param>
    /// <param name="actorId">Identificador del actor cuando no proviene del token.</param>
    Task LogAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        Guid? tenantId = null,
        object? metadata = null,
        bool success = true,
        string? errorMessage = null,
        string? actorName = null,
        Guid? actorId = null,
        CancellationToken ct = default);
}
