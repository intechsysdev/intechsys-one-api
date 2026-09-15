using One.Domain.Common;
using One.Domain.Enums;
using One.Domain.Identity;

namespace One.Domain.Entities;

/// <summary>Pertenencia de un usuario a una empresa, con su rol dentro de ella.</summary>
public class TenantUser : AuditableEntity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public TenantRole Role { get; set; } = TenantRole.Member;

    public bool IsActive { get; set; } = true;

    /// <summary>Empresa que el usuario ve al entrar al portal.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset? InvitedAt { get; set; }
    public Guid? InvitedBy { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
}
