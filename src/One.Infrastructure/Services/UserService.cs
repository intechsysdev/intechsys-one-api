using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Entities;
using One.Domain.Identity;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>Administración de usuarios de plataforma y de su pertenencia a empresas.</summary>
public sealed class UserService(
    OneDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IAuditService audit,
    ICurrentUser currentUser) : IUserService
{
    public async Task<PagedResult<UserListItemDto>> ListAsync(
        PageRequest page, Guid? tenantId, bool? isActive, string? role, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();

        if (tenantId is { } tid)
            query = query.Where(u => u.Memberships.Any(m => m.TenantId == tid));

        if (isActive is not null)
            query = query.Where(u => u.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleIds = db.Roles.Where(r => r.Name == role).Select(r => r.Id);
            var userIds = db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId);
            query = query.Where(u => userIds.Contains(u.Id));
        }

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var term = page.Search.Trim();
            query = query.Where(u =>
                EF.Functions.Like(u.FirstName, $"%{term}%") ||
                EF.Functions.Like(u.LastName, $"%{term}%") ||
                (u.Email != null && EF.Functions.Like(u.Email, $"%{term}%")) ||
                (u.JobTitle != null && EF.Functions.Like(u.JobTitle, $"%{term}%")));
        }

        query = (page.SortBy?.ToLowerInvariant(), page.Descending) switch
        {
            ("name", true) => query.OrderByDescending(u => u.FirstName).ThenByDescending(u => u.LastName),
            ("name", false) => query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName),
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("email", false) => query.OrderBy(u => u.Email),
            ("lastlogin", false) => query.OrderBy(u => u.LastLoginAt),
            ("lastlogin", true) => query.OrderByDescending(u => u.LastLoginAt),
            ("createdat", false) => query.OrderBy(u => u.CreatedAt),
            _ => query.OrderByDescending(u => u.CreatedAt)
        };

        var total = await query.CountAsync(ct);

        var rows = await query
            .Skip(page.Skip).Take(page.PageSize)
            .Select(u => new
            {
                User = u,
                TenantCount = u.Memberships.Count(m => m.IsActive),
                Roles = db.UserRoles.Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
                    .ToList()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new UserListItemDto(
            r.User.Id, r.User.Email ?? string.Empty, r.User.FullName, r.User.AvatarUrl, r.User.JobTitle,
            r.User.IsActive, IsLockedOut(r.User), r.Roles, r.TenantCount, r.User.CreatedAt, r.User.LastLoginAt)).ToList();

        return new PagedResult<UserListItemDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<OperationResult<UserDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null
            ? OperationResult<UserDto>.NotFound("El usuario no existe.")
            : OperationResult<UserDto>.Ok(await BuildDtoAsync(user, ct));
    }

    public async Task<OperationResult<(UserDto User, string? TemporaryPassword)>> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await userManager.FindByEmailAsync(email) is not null)
            return OperationResult<(UserDto, string?)>.Conflict("Ya existe un usuario con ese correo.");

        var generated = string.IsNullOrWhiteSpace(request.Password);
        var password = generated ? GeneratePassword() : request.Password!;

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            JobTitle = request.JobTitle?.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            AvatarUrl = request.AvatarUrl?.Trim(),
            TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "America/Bogota" : request.TimeZone.Trim(),
            Locale = string.IsNullOrWhiteSpace(request.Locale) ? "es-CO" : request.Locale.Trim(),
            IsActive = request.IsActive,
            MustChangePassword = generated || request.MustChangePassword
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
            return OperationResult<(UserDto, string?)>.Invalid("No se pudo crear el usuario.", AuthService.ToErrors(created));

        var roleResult = await SyncRolesAsync(user, request.Roles);
        if (!roleResult.Succeeded)
            return OperationResult<(UserDto, string?)>.Invalid(roleResult.Message!, roleResult.Errors);

        foreach (var membership in request.Memberships)
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == membership.TenantId, ct)) continue;

            db.TenantUsers.Add(new TenantUser
            {
                TenantId = membership.TenantId,
                UserId = user.Id,
                Role = membership.Role,
                IsDefault = membership.IsDefault,
                JoinedAt = DateTimeOffset.UtcNow,
                InvitedBy = currentUser.UserId,
                CreatedBy = currentUser.UserId
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("user.created", nameof(ApplicationUser), user.Id.ToString(),
            metadata: new { user.Email, Roles = request.Roles, Tenants = request.Memberships.Count }, ct: ct);

        return OperationResult<(UserDto, string?)>.Ok((await BuildDtoAsync(user, ct), generated ? password : null));
    }

    public async Task<OperationResult<UserDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return OperationResult<UserDto>.NotFound("El usuario no existe.");

        var deactivatingSelf = !request.IsActive && currentUser.UserId == id;
        if (deactivatingSelf)
            return OperationResult<UserDto>.Conflict("No puede desactivar su propia cuenta.");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.JobTitle = request.JobTitle?.Trim();
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.AvatarUrl = request.AvatarUrl?.Trim();
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.TimeZone)) user.TimeZone = request.TimeZone.Trim();
        if (!string.IsNullOrWhiteSpace(request.Locale)) user.Locale = request.Locale.Trim();

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return OperationResult<UserDto>.Invalid("No se pudo actualizar el usuario.", AuthService.ToErrors(updated));

        var roleResult = await SyncRolesAsync(user, request.Roles);
        if (!roleResult.Succeeded)
            return OperationResult<UserDto>.Invalid(roleResult.Message!, roleResult.Errors);

        if (!request.IsActive)
        {
            await db.RefreshTokens
                .Where(t => t.UserId == id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        }

        await audit.LogAsync("user.updated", nameof(ApplicationUser), id.ToString(),
            metadata: new { user.Email, request.IsActive, Roles = request.Roles }, ct: ct);

        return OperationResult<UserDto>.Ok(await BuildDtoAsync(user, ct));
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (currentUser.UserId == id)
            return OperationResult.Conflict("No puede eliminar su propia cuenta.");

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return OperationResult.NotFound("El usuario no existe.");

        var orphanedTenants = await db.TenantUsers
            .Where(m => m.UserId == id && m.Role == Domain.Enums.TenantRole.Owner)
            .Where(m => !m.Tenant.Members.Any(o => o.UserId != id && o.Role == Domain.Enums.TenantRole.Owner && o.IsActive))
            .Select(m => m.Tenant.Name)
            .ToListAsync(ct);

        if (orphanedTenants.Count > 0)
            return OperationResult.Conflict($"El usuario es el único propietario de: {string.Join(", ", orphanedTenants)}. Asigne otro propietario antes de eliminarlo.");

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return OperationResult.Invalid("No se pudo eliminar el usuario.", AuthService.ToErrors(result));

        await audit.LogAsync("user.deleted", nameof(ApplicationUser), id.ToString(), metadata: new { user.Email }, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<TemporaryPasswordDto>> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return OperationResult<TemporaryPasswordDto>.NotFound("El usuario no existe.");

        var generated = string.IsNullOrWhiteSpace(request.NewPassword);
        var password = generated ? GeneratePassword() : request.NewPassword!;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, password);

        if (!result.Succeeded)
            return OperationResult<TemporaryPasswordDto>.Invalid("No se pudo restablecer la contraseña.", AuthService.ToErrors(result));

        user.MustChangePassword = request.MustChangePassword || generated;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        await db.RefreshTokens
            .Where(t => t.UserId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await audit.LogAsync("user.password_reset", nameof(ApplicationUser), id.ToString(), metadata: new { user.Email }, ct: ct);

        return OperationResult<TemporaryPasswordDto>.Ok(
            new TemporaryPasswordDto(id, user.Email ?? string.Empty, generated ? password : null));
    }

    public async Task<OperationResult> SetLockoutAsync(Guid id, bool locked, CancellationToken ct = default)
    {
        if (currentUser.UserId == id)
            return OperationResult.Conflict("No puede bloquear su propia cuenta.");

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return OperationResult.NotFound("El usuario no existe.");

        var result = await userManager.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.UtcNow.AddYears(100) : null);
        if (!result.Succeeded)
            return OperationResult.Invalid("No se pudo cambiar el bloqueo.", AuthService.ToErrors(result));

        if (!locked) await userManager.ResetAccessFailedCountAsync(user);

        await audit.LogAsync(locked ? "user.locked" : "user.unlocked", nameof(ApplicationUser), id.ToString(),
            metadata: new { user.Email }, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default) =>
        await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(
                r.Id, r.Name ?? string.Empty, r.Description, r.IsSystem,
                db.UserRoles.Count(ur => ur.RoleId == r.Id)))
            .ToListAsync(ct);

    private async Task<OperationResult> SyncRolesAsync(ApplicationUser user, IReadOnlyList<string> requested)
    {
        var desired = requested.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();

        foreach (var role in desired)
        {
            if (!await roleManager.RoleExistsAsync(role))
                return OperationResult.Invalid($"El rol {role} no existe.");
        }

        var current = await userManager.GetRolesAsync(user);

        var toRemove = current.Except(desired, StringComparer.OrdinalIgnoreCase).ToList();
        if (toRemove.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded)
                return OperationResult.Invalid("No se pudieron retirar los roles.", AuthService.ToErrors(removed));
        }

        var toAdd = desired.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
        if (toAdd.Count > 0)
        {
            var added = await userManager.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded)
                return OperationResult.Invalid("No se pudieron asignar los roles.", AuthService.ToErrors(added));
        }

        return OperationResult.Ok();
    }

    private async Task<UserDto> BuildDtoAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);

        var memberships = await db.TenantUsers.AsNoTracking()
            .Include(m => m.Tenant)
            .Where(m => m.UserId == user.Id)
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.Tenant.Name)
            .ToListAsync(ct);

        return new UserDto(
            user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName, user.FullName,
            user.AvatarUrl, user.JobTitle, user.PhoneNumber, user.IsActive, user.EmailConfirmed,
            user.TwoFactorEnabled, user.MustChangePassword, IsLockedOut(user), user.TimeZone, user.Locale,
            [.. roles],
            [.. memberships.Select(m => m.ToMembershipDto())],
            user.CreatedAt, user.LastLoginAt);
    }

    private static bool IsLockedOut(ApplicationUser user) =>
        user.LockoutEnabled && user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;

    /// <summary>Contraseña temporal que satisface la política de Identity sin caracteres ambiguos.</summary>
    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*?-_";

        var chars = new List<char>
        {
            Pick(upper), Pick(upper),
            Pick(lower), Pick(lower), Pick(lower), Pick(lower),
            Pick(digits), Pick(digits), Pick(digits),
            Pick(symbols), Pick(symbols)
        };

        // Barajado Fisher-Yates para que las clases de caracteres no queden en posiciones fijas.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string([.. chars]);

        static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }
}
