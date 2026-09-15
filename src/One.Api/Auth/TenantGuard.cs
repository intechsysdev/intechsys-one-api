using One.Domain.Enums;
using One.Domain.Identity;

namespace One.Api.Auth;

/// <summary>Nombres de las políticas de autorización de la plataforma.</summary>
public static class Policies
{
    /// <summary>Operaciones que alteran el catálogo global o las cuentas de usuario.</summary>
    public const string PlatformAdmin = "platform:admin";

    /// <summary>Lectura transversal, incluida la de soporte.</summary>
    public const string PlatformRead = "platform:read";
}

/// <summary>
/// Decide si el usuario autenticado puede ver o administrar una empresa concreta.
/// El administrador de plataforma pasa siempre; el resto depende de su rol en esa empresa.
/// </summary>
public sealed class TenantGuard(CurrentUser currentUser)
{
    public bool CanRead(Guid tenantId) =>
        currentUser.IsPlatformAdmin
        || currentUser.Roles.Contains(PlatformRoles.PlatformSupport)
        || currentUser.Memberships.ContainsKey(tenantId);

    /// <summary>Escribir configuración exige ser propietario o administrador de la empresa.</summary>
    public bool CanManage(Guid tenantId) =>
        currentUser.IsPlatformAdmin
        || (currentUser.Memberships.TryGetValue(tenantId, out var role) && role is TenantRole.Owner or TenantRole.Admin);

    /// <summary>
    /// Revelar secretos es más estricto que administrar: soporte nunca los ve y un
    /// miembro sin rol de gestión tampoco.
    /// </summary>
    public bool CanRevealSecrets(Guid tenantId) => CanManage(tenantId);
}
