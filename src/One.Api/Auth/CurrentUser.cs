using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using One.Application.Contracts;
using One.Domain.Enums;
using One.Domain.Identity;

namespace One.Api.Auth;

/// <summary>Lee la identidad del usuario desde el JWT de la petición en curso.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>Claim con la pertenencia a una empresa, con formato "{tenantId}:{rol}".</summary>
    public const string TenantClaimType = "tenant";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public string? Email =>
        Principal?.FindFirstValue(JwtRegisteredClaimNames.Email) ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public string? DisplayName =>
        Principal?.FindFirstValue(JwtRegisteredClaimNames.Name) ?? Principal?.FindFirstValue(ClaimTypes.Name) ?? Email;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsPlatformAdmin => Principal?.IsInRole(PlatformRoles.PlatformAdmin) ?? false;

    public IReadOnlyList<string> Roles =>
        Principal is null ? [] : [.. Principal.FindAll(ClaimTypes.Role).Select(c => c.Value)];

    public string? IpAddress
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null) return null;

            // Detrás de un proxy inverso la IP real llega en X-Forwarded-For.
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    /// <summary>Pertenencias a empresas embebidas en el token, con el rol de cada una.</summary>
    public IReadOnlyDictionary<Guid, TenantRole> Memberships
    {
        get
        {
            if (Principal is null) return new Dictionary<Guid, TenantRole>();

            var result = new Dictionary<Guid, TenantRole>();

            foreach (var claim in Principal.FindAll(TenantClaimType))
            {
                var parts = claim.Value.Split(':', 2);
                if (parts.Length != 2) continue;
                if (!Guid.TryParse(parts[0], out var tenantId)) continue;
                if (!Enum.TryParse<TenantRole>(parts[1], ignoreCase: true, out var role)) continue;

                result[tenantId] = role;
            }

            return result;
        }
    }
}
