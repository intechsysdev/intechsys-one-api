using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Entities;
using One.Domain.Identity;
using One.Infrastructure.Options;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>Autenticación del portal: login, rotación de refresh tokens y perfil propio.</summary>
public sealed class AuthService(
    OneDbContext db,
    UserManager<ApplicationUser> userManager,
    ITokenService tokens,
    IAuditService audit,
    ICurrentUser currentUser,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<OperationResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        // Mismo mensaje para usuario inexistente y contraseña incorrecta: no se revela
        // qué correos están dados de alta.
        if (user is null)
        {
            await audit.LogAsync("auth.login_failed", metadata: new { request.Email, Reason = "usuario inexistente" },
                success: false, errorMessage: "Usuario inexistente", actorName: request.Email.Trim(), ct: ct);

            return OperationResult<AuthResponse>.Unauthorized("Correo o contraseña incorrectos.");
        }

        if (!user.IsActive)
            return OperationResult<AuthResponse>.Forbidden("La cuenta está desactivada. Contacte al administrador.");

        if (await userManager.IsLockedOutAsync(user))
        {
            await audit.LogAsync("auth.login_failed", metadata: new { request.Email, Reason = "cuenta bloqueada" },
                success: false, errorMessage: "Cuenta bloqueada", actorName: user.FullName, actorId: user.Id, ct: ct);

            return OperationResult<AuthResponse>.Forbidden("La cuenta está bloqueada temporalmente por intentos fallidos.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);

            await audit.LogAsync("auth.login_failed", metadata: new { request.Email, Reason = "contraseña incorrecta" },
                success: false, errorMessage: "Contraseña incorrecta", actorName: user.FullName, actorId: user.Id, ct: ct);

            return await userManager.IsLockedOutAsync(user)
                ? OperationResult<AuthResponse>.Forbidden("La cuenta quedó bloqueada por demasiados intentos fallidos.")
                : OperationResult<AuthResponse>.Unauthorized("Correo o contraseña incorrectos.");
        }

        await userManager.ResetAccessFailedCountAsync(user);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.LastLoginIp = currentUser.IpAddress;
        await userManager.UpdateAsync(user);

        var response = await IssueAsync(user, ct);
        await audit.LogAsync("auth.login", nameof(ApplicationUser), user.Id.ToString(), metadata: new { user.Email },
            actorName: user.FullName, actorId: user.Id, ct: ct);

        return OperationResult<AuthResponse>.Ok(response);
    }

    public async Task<OperationResult<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            return OperationResult<AuthResponse>.Unauthorized("El token de refresco no es válido.");

        if (!stored.IsActive)
        {
            // Reutilizar un token ya rotado apunta a robo: se cierran todas las sesiones.
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow)
                    .SetProperty(t => t.RevokedByIp, currentUser.IpAddress), ct);

            await audit.LogAsync("auth.refresh_reuse_detected", nameof(RefreshToken), stored.Id.ToString(),
                metadata: new { stored.UserId }, success: false, errorMessage: "Reutilización de token", ct: ct);

            return OperationResult<AuthResponse>.Unauthorized("La sesión expiró. Vuelva a iniciar sesión.");
        }

        if (!stored.User.IsActive)
            return OperationResult<AuthResponse>.Forbidden("La cuenta está desactivada.");

        var response = await IssueAsync(stored.User, ct, stored);

        return OperationResult<AuthResponse>.Ok(response);
    }

    public async Task<OperationResult> LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return OperationResult.Ok();

        var hash = tokens.HashRefreshToken(refreshToken);

        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(t => t.RevokedByIp, currentUser.IpAddress), ct);

        await audit.LogAsync("auth.logout", nameof(ApplicationUser), currentUser.UserId?.ToString(), ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<CurrentUserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return OperationResult<CurrentUserDto>.NotFound("El usuario no existe.");

        return OperationResult<CurrentUserDto>.Ok(await BuildCurrentUserAsync(user, ct));
    }

    public async Task<OperationResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return OperationResult.NotFound("El usuario no existe.");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
            return OperationResult.Invalid("No se pudo cambiar la contraseña.", ToErrors(result));

        user.MustChangePassword = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        // Cambiar la contraseña invalida las sesiones abiertas en otros dispositivos.
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(t => t.RevokedByIp, currentUser.IpAddress), ct);

        await audit.LogAsync("auth.password_changed", nameof(ApplicationUser), userId.ToString(), ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<CurrentUserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return OperationResult<CurrentUserDto>.NotFound("El usuario no existe.");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.JobTitle = request.JobTitle?.Trim();
        user.AvatarUrl = request.AvatarUrl?.Trim();
        user.TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? user.TimeZone : request.TimeZone.Trim();
        user.Locale = string.IsNullOrWhiteSpace(request.Locale) ? user.Locale : request.Locale.Trim();
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.profile_updated", nameof(ApplicationUser), userId.ToString(), ct: ct);

        return OperationResult<CurrentUserDto>.Ok(await BuildCurrentUserAsync(user, ct));
    }

    private async Task<AuthResponse> IssueAsync(ApplicationUser user, CancellationToken ct, RefreshToken? rotating = null)
    {
        var roles = await userManager.GetRolesAsync(user);

        var memberships = await db.TenantUsers.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .Select(m => new { m.TenantId, m.Role })
            .ToListAsync(ct);

        var accessToken = tokens.CreateAccessToken(
            user.Id, user.Email ?? string.Empty, user.FullName, roles,
            memberships.Select(m => (m.TenantId, m.Role)));

        var refreshValue = tokens.CreateRefreshToken();
        var refreshHash = tokens.HashRefreshToken(refreshValue);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays),
            CreatedByIp = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent is { Length: > 400 } ua ? ua[..400] : currentUser.UserAgent
        });

        if (rotating is not null)
        {
            rotating.RevokedAt = DateTimeOffset.UtcNow;
            rotating.RevokedByIp = currentUser.IpAddress;
            rotating.ReplacedByTokenHash = refreshHash;
        }

        await PurgeExpiredTokensAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);

        return new AuthResponse(
            accessToken.Value,
            refreshValue,
            accessToken.ExpiresAt,
            accessToken.ExpiresInSeconds,
            await BuildCurrentUserAsync(user, ct));
    }

    private async Task<CurrentUserDto> BuildCurrentUserAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);

        var memberships = await db.TenantUsers.AsNoTracking()
            .Include(m => m.Tenant)
            .Where(m => m.UserId == user.Id && m.IsActive)
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.Tenant.Name)
            .ToListAsync(ct);

        return new CurrentUserDto(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.AvatarUrl,
            user.JobTitle,
            roles.Contains(PlatformRoles.PlatformAdmin),
            [.. roles],
            [.. memberships.Select(m => m.ToMembershipDto())]);
    }

    private Task PurgeExpiredTokensAsync(Guid userId, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.ExpiresAt < DateTimeOffset.UtcNow.AddDays(-30))
            .ExecuteDeleteAsync(ct);

    internal static Dictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
