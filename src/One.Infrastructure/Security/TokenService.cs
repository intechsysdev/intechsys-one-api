using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using One.Application.Contracts;
using One.Domain.Enums;
using One.Infrastructure.Options;

namespace One.Infrastructure.Security;

/// <summary>Emite el JWT del portal y los tokens de refresco rotatorios.</summary>
public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    /// <summary>Claim con la pertenencia a una empresa, con formato "{tenantId}:{rol}".</summary>
    public const string TenantClaimType = "tenant";

    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(
        Guid userId,
        string email,
        string fullName,
        IEnumerable<string> roles,
        IEnumerable<(Guid TenantId, TenantRole Role)> memberships)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, fullName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(memberships.Select(m => new Claim(TenantClaimType, $"{m.TenantId}:{m.Role}")));

        var credentials = new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            _options.AccessTokenMinutes * 60);
    }

    public string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(_options.SigningKey));
}
