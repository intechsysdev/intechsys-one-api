using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Entities;
using One.Domain.Enums;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>Alta, mantenimiento y composición de miembros de las empresas.</summary>
public sealed class TenantService(OneDbContext db, IAuditService audit, ICurrentUser currentUser) : ITenantService
{
    public async Task<PagedResult<TenantListItemDto>> ListAsync(
        PageRequest page, TenantStatus? status, IReadOnlyCollection<Guid>? restrictToTenantIds, CancellationToken ct = default)
    {
        var query = db.Tenants.AsNoTracking();

        // Un usuario sin rol de plataforma solo ve las empresas a las que pertenece.
        if (restrictToTenantIds is not null)
        {
            if (restrictToTenantIds.Count == 0)
                return PagedResult<TenantListItemDto>.Empty(page.Page, page.PageSize);

            var visible = restrictToTenantIds.ToList();
            query = query.Where(t => visible.Contains(t.Id));
        }

        if (status is not null)
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var term = page.Search.Trim();
            query = query.Where(t =>
                EF.Functions.Like(t.Name, $"%{term}%") ||
                EF.Functions.Like(t.Slug, $"%{term}%") ||
                (t.LegalName != null && EF.Functions.Like(t.LegalName, $"%{term}%")) ||
                (t.TaxId != null && EF.Functions.Like(t.TaxId, $"%{term}%")) ||
                (t.ContactEmail != null && EF.Functions.Like(t.ContactEmail, $"%{term}%")));
        }

        query = (page.SortBy?.ToLowerInvariant(), page.Descending) switch
        {
            ("name", true) => query.OrderByDescending(t => t.Name),
            ("name", false) => query.OrderBy(t => t.Name),
            ("status", true) => query.OrderByDescending(t => t.Status).ThenBy(t => t.Name),
            ("status", false) => query.OrderBy(t => t.Status).ThenBy(t => t.Name),
            ("createdat", false) => query.OrderBy(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };

        var total = await query.CountAsync(ct);

        var items = await query
            .Skip(page.Skip).Take(page.PageSize)
            .Select(t => new TenantListItemDto(
                t.Id, t.Name, t.Slug, t.LogoUrl, t.BrandColor, t.Status, t.Plan, t.ContactEmail,
                t.Members.Count(m => m.IsActive),
                t.Apps.Count(a => a.IsEnabled),
                t.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<TenantListItemDto>(items, page.Page, page.PageSize, total);
    }

    public Task<OperationResult<TenantDto>> GetAsync(Guid id, CancellationToken ct = default) =>
        LoadDtoAsync(t => t.Id == id, ct);

    public Task<OperationResult<TenantDto>> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        LoadDtoAsync(t => t.Slug == slug, ct);

    public async Task<OperationResult<TenantDto>> CreateAsync(CreateTenantRequest request, CancellationToken ct = default)
    {
        var requestedSlug = request.Slug?.Trim();
        var baseSlug = string.IsNullOrWhiteSpace(requestedSlug) ? Slugger.From(request.Name) : requestedSlug;

        if (string.IsNullOrWhiteSpace(baseSlug))
            return OperationResult<TenantDto>.Invalid("No se pudo derivar un identificador válido del nombre.");

        var taken = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Slug.StartsWith(baseSlug))
            .Select(t => t.Slug)
            .ToListAsync(ct);

        // Un slug explícito se respeta o se rechaza; uno derivado del nombre se desambigua solo.
        if (!string.IsNullOrWhiteSpace(requestedSlug) && taken.Contains(baseSlug, StringComparer.OrdinalIgnoreCase))
            return OperationResult<TenantDto>.Conflict($"Ya existe una empresa con el identificador {requestedSlug}.");

        var slug = Slugger.MakeUnique(baseSlug, candidate => taken.Contains(candidate, StringComparer.OrdinalIgnoreCase));

        var tenant = new Tenant
        {
            Name = request.Name.Trim(),
            Slug = slug,
            LegalName = request.LegalName?.Trim(),
            TaxId = request.TaxId?.Trim(),
            ContactEmail = request.ContactEmail?.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            Website = request.Website?.Trim(),
            LogoUrl = request.LogoUrl?.Trim(),
            BrandColor = request.BrandColor?.Trim(),
            Country = request.Country?.Trim(),
            City = request.City?.Trim(),
            Address = request.Address?.Trim(),
            Status = request.Status,
            Plan = request.Plan?.Trim(),
            Notes = request.Notes?.Trim(),
            MaxApps = request.MaxApps,
            MaxUsers = request.MaxUsers,
            CreatedBy = currentUser.UserId
        };

        db.Tenants.Add(tenant);

        if (request.OwnerUserId is { } ownerId)
        {
            if (!await db.Users.AnyAsync(u => u.Id == ownerId, ct))
                return OperationResult<TenantDto>.Invalid("El usuario indicado como propietario no existe.");

            db.TenantUsers.Add(new TenantUser
            {
                TenantId = tenant.Id,
                UserId = ownerId,
                Role = TenantRole.Owner,
                JoinedAt = DateTimeOffset.UtcNow,
                IsDefault = true,
                CreatedBy = currentUser.UserId
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("tenant.created", nameof(Tenant), tenant.Id.ToString(), tenant.Id,
            new { tenant.Name, tenant.Slug, Status = tenant.Status.ToString() }, ct: ct);

        return await LoadDtoAsync(t => t.Id == tenant.Id, ct);
    }

    public async Task<OperationResult<TenantDto>> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return OperationResult<TenantDto>.NotFound("La empresa no existe.");

        tenant.Name = request.Name.Trim();
        tenant.LegalName = request.LegalName?.Trim();
        tenant.TaxId = request.TaxId?.Trim();
        tenant.ContactEmail = request.ContactEmail?.Trim();
        tenant.ContactPhone = request.ContactPhone?.Trim();
        tenant.Website = request.Website?.Trim();
        tenant.LogoUrl = request.LogoUrl?.Trim();
        tenant.BrandColor = request.BrandColor?.Trim();
        tenant.Country = request.Country?.Trim();
        tenant.City = request.City?.Trim();
        tenant.Address = request.Address?.Trim();
        tenant.Status = request.Status;
        tenant.Plan = request.Plan?.Trim();
        tenant.Notes = request.Notes?.Trim();
        tenant.MaxApps = request.MaxApps;
        tenant.MaxUsers = request.MaxUsers;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("tenant.updated", nameof(Tenant), tenant.Id.ToString(), tenant.Id,
            new { tenant.Name, Status = tenant.Status.ToString() }, ct: ct);

        return await LoadDtoAsync(t => t.Id == id, ct);
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return OperationResult.NotFound("La empresa no existe.");

        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTimeOffset.UtcNow;
        tenant.DeletedBy = currentUser.UserId;
        tenant.Status = TenantStatus.Archived;

        await db.SaveChangesAsync(ct);

        // Cortar el acceso de las integraciones es parte del archivado: de lo contrario las apps
        // seguirían resolviendo configuración de una empresa que ya no está operativa.
        await db.ApiCredentials.IgnoreQueryFilters()
            .Where(c => c.TenantApp.TenantId == id && c.Status == CredentialStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CredentialStatus.Revoked)
                .SetProperty(c => c.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(c => c.RevokedReason, "Empresa archivada"), ct);

        await audit.LogAsync("tenant.deleted", nameof(Tenant), id.ToString(), id, new { tenant.Slug }, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<IReadOnlyList<TenantMemberDto>>> ListMembersAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            return OperationResult<IReadOnlyList<TenantMemberDto>>.NotFound("La empresa no existe.");

        var members = await db.TenantUsers.AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.Role).ThenBy(m => m.User.FirstName)
            .ToListAsync(ct);

        return OperationResult<IReadOnlyList<TenantMemberDto>>.Ok([.. members.Select(m => m.ToDto())]);
    }

    public async Task<OperationResult<TenantMemberDto>> AddMemberAsync(Guid tenantId, AddTenantMemberRequest request, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return OperationResult<TenantMemberDto>.NotFound("La empresa no existe.");

        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
            return OperationResult<TenantMemberDto>.NotFound("El usuario no existe.");

        if (await db.TenantUsers.AnyAsync(m => m.TenantId == tenantId && m.UserId == request.UserId, ct))
            return OperationResult<TenantMemberDto>.Conflict("El usuario ya pertenece a esta empresa.");

        if (tenant.MaxUsers is { } max && await db.TenantUsers.CountAsync(m => m.TenantId == tenantId && m.IsActive, ct) >= max)
            return OperationResult<TenantMemberDto>.Conflict($"La empresa alcanzó su límite de {max} usuarios.");

        var membership = new TenantUser
        {
            TenantId = tenantId,
            UserId = request.UserId,
            Role = request.Role,
            IsDefault = request.IsDefault,
            JoinedAt = DateTimeOffset.UtcNow,
            InvitedAt = DateTimeOffset.UtcNow,
            InvitedBy = currentUser.UserId,
            CreatedBy = currentUser.UserId
        };

        db.TenantUsers.Add(membership);
        await db.SaveChangesAsync(ct);

        if (request.IsDefault)
            await ClearOtherDefaultsAsync(request.UserId, membership.Id, ct);

        await audit.LogAsync("tenant.member_added", nameof(TenantUser), membership.Id.ToString(), tenantId,
            new { request.UserId, Role = request.Role.ToString() }, ct: ct);

        var created = await db.TenantUsers.AsNoTracking().Include(m => m.User)
            .FirstAsync(m => m.Id == membership.Id, ct);

        return OperationResult<TenantMemberDto>.Ok(created.ToDto());
    }

    public async Task<OperationResult<TenantMemberDto>> UpdateMemberAsync(Guid tenantId, Guid membershipId, UpdateTenantMemberRequest request, CancellationToken ct = default)
    {
        var membership = await db.TenantUsers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.TenantId == tenantId, ct);

        if (membership is null) return OperationResult<TenantMemberDto>.NotFound("La pertenencia no existe.");

        // Una empresa sin propietario activo se queda sin nadie que pueda administrarla.
        var losesOwner = membership.Role == TenantRole.Owner && (request.Role != TenantRole.Owner || !request.IsActive);
        if (losesOwner && !await HasOtherOwnerAsync(tenantId, membershipId, ct))
            return OperationResult<TenantMemberDto>.Conflict("La empresa debe conservar al menos un propietario activo.");

        membership.Role = request.Role;
        membership.IsActive = request.IsActive;
        membership.IsDefault = request.IsDefault;
        membership.UpdatedAt = DateTimeOffset.UtcNow;
        membership.UpdatedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);

        if (request.IsDefault)
            await ClearOtherDefaultsAsync(membership.UserId, membershipId, ct);

        await audit.LogAsync("tenant.member_updated", nameof(TenantUser), membershipId.ToString(), tenantId,
            new { Role = request.Role.ToString(), request.IsActive }, ct: ct);

        return OperationResult<TenantMemberDto>.Ok(membership.ToDto());
    }

    public async Task<OperationResult> RemoveMemberAsync(Guid tenantId, Guid membershipId, CancellationToken ct = default)
    {
        var membership = await db.TenantUsers.FirstOrDefaultAsync(m => m.Id == membershipId && m.TenantId == tenantId, ct);
        if (membership is null) return OperationResult.NotFound("La pertenencia no existe.");

        if (membership.Role == TenantRole.Owner && !await HasOtherOwnerAsync(tenantId, membershipId, ct))
            return OperationResult.Conflict("La empresa debe conservar al menos un propietario activo.");

        db.TenantUsers.Remove(membership);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("tenant.member_removed", nameof(TenantUser), membershipId.ToString(), tenantId,
            new { membership.UserId }, ct: ct);

        return OperationResult.Ok();
    }

    private Task<bool> HasOtherOwnerAsync(Guid tenantId, Guid excludedMembershipId, CancellationToken ct) =>
        db.TenantUsers.AnyAsync(
            m => m.TenantId == tenantId && m.Id != excludedMembershipId && m.Role == TenantRole.Owner && m.IsActive,
            ct);

    private Task ClearOtherDefaultsAsync(Guid userId, Guid keepMembershipId, CancellationToken ct) =>
        db.TenantUsers
            .Where(m => m.UserId == userId && m.Id != keepMembershipId && m.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDefault, false), ct);

    private async Task<OperationResult<TenantDto>> LoadDtoAsync(Expression<Func<Tenant, bool>> predicate, CancellationToken ct)
    {
        var projection = await db.Tenants.AsNoTracking()
            .Where(predicate)
            .Select(t => new
            {
                Tenant = t,
                UserCount = t.Members.Count(m => m.IsActive),
                AppCount = t.Apps.Count(),
                CredentialCount = t.Apps.SelectMany(a => a.Credentials).Count(c => c.Status == CredentialStatus.Active)
            })
            .FirstOrDefaultAsync(ct);

        return projection is null
            ? OperationResult<TenantDto>.NotFound("La empresa no existe.")
            : OperationResult<TenantDto>.Ok(projection.Tenant.ToDto(projection.UserCount, projection.AppCount, projection.CredentialCount));
    }
}
