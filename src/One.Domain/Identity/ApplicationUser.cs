using Microsoft.AspNetCore.Identity;
using One.Domain.Entities;

namespace One.Domain.Identity;

/// <summary>Usuario de la plataforma. Mapea sobre AspNetUsers.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? JobTitle { get; set; }
    public string TimeZone { get; set; } = "America/Bogota";
    public string Locale { get; set; } = "es-CO";

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }

    public string FullName => string.Join(' ', new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public ICollection<TenantUser> Memberships { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
