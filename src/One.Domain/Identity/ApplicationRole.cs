using Microsoft.AspNetCore.Identity;

namespace One.Domain.Identity;

/// <summary>Rol global de plataforma. Mapea sobre AspNetRoles.</summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string name) : base(name) { }

    public string? Description { get; set; }

    /// <summary>Los roles de sistema no se pueden eliminar desde el portal.</summary>
    public bool IsSystem { get; set; }
}
