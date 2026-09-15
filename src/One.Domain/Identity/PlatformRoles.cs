namespace One.Domain.Identity;

/// <summary>Roles globales de la plataforma (independientes del tenant).</summary>
public static class PlatformRoles
{
    /// <summary>Control total: crea empresas, apps y usuarios.</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Lectura transversal para soporte, sin acceso a secretos en claro.</summary>
    public const string PlatformSupport = "PlatformSupport";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [PlatformAdmin] = "Administrador de la plataforma: gestiona empresas, apps, credenciales y usuarios.",
        [PlatformSupport] = "Soporte: consulta transversal de empresas y apps sin revelar secretos."
    };
}
