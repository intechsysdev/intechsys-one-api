using Microsoft.EntityFrameworkCore;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Enums;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>Métricas agregadas y traza de actividad para la portada del portal.</summary>
public sealed class DashboardService(OneDbContext db) : IDashboardService
{
    private const int ExpiringWindowDays = 30;

    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiringBefore = now.AddDays(ExpiringWindowDays);

        var tenantStats = await db.Tenants.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Active = g.Count(t => t.Status == TenantStatus.Active) })
            .FirstOrDefaultAsync(ct);

        var appStats = await db.Apps.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Active = g.Count(a => a.IsActive) })
            .FirstOrDefaultAsync(ct);

        var userStats = await db.Users.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Active = g.Count(u => u.IsActive) })
            .FirstOrDefaultAsync(ct);

        var credentialStats = await db.ApiCredentials.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Active = g.Count(c => c.Status == CredentialStatus.Active),
                Revoked = g.Count(c => c.Status == CredentialStatus.Revoked),
                Expiring = g.Count(c => c.Status == CredentialStatus.Active && c.ExpiresAt != null && c.ExpiresAt <= expiringBefore)
            })
            .FirstOrDefaultAsync(ct);

        var subscriptions = await db.TenantApps.AsNoTracking().CountAsync(ct);

        // El orden se aplica sobre las columnas, no sobre el DTO ya proyectado:
        // ordenar por una propiedad del record no es traducible a SQL.
        var topTenants = await db.Tenants.AsNoTracking()
            .OrderByDescending(t => t.Apps.Count())
            .ThenByDescending(t => t.Members.Count(m => m.IsActive))
            .Take(6)
            .Select(t => new TenantAdoptionDto(
                t.Id, t.Name, t.Slug, t.LogoUrl, t.BrandColor,
                t.Apps.Count(), t.Members.Count(m => m.IsActive)))
            .ToListAsync(ct);

        var topApps = await db.Apps.AsNoTracking()
            .OrderByDescending(a => a.Subscriptions.Count())
            .ThenByDescending(a => a.Subscriptions.SelectMany(s => s.Credentials).Count(c => c.Status == CredentialStatus.Active))
            .Take(6)
            .Select(a => new AppAdoptionDto(
                a.Id, a.Name, a.Slug, a.IconUrl, a.Color,
                a.Subscriptions.Count(),
                a.Subscriptions.SelectMany(s => s.Credentials).Count(c => c.Status == CredentialStatus.Active)))
            .ToListAsync(ct);

        var recent = await ListAuditLogsAsync(new PageRequest { Page = 1, PageSize = 12 }, null, null, ct);

        return new DashboardStatsDto(
            tenantStats?.Total ?? 0, tenantStats?.Active ?? 0,
            appStats?.Total ?? 0, appStats?.Active ?? 0,
            userStats?.Total ?? 0, userStats?.Active ?? 0,
            subscriptions,
            credentialStats?.Active ?? 0,
            credentialStats?.Expiring ?? 0,
            credentialStats?.Revoked ?? 0,
            topTenants, topApps, recent.Items);
    }

    public async Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(
        PageRequest page, Guid? tenantId, string? action, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking();

        if (tenantId is not null)
            query = query.Where(l => l.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action.StartsWith(action));

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var term = page.Search.Trim();
            query = query.Where(l =>
                EF.Functions.Like(l.Action, $"%{term}%") ||
                (l.ActorName != null && EF.Functions.Like(l.ActorName, $"%{term}%")) ||
                (l.EntityType != null && EF.Functions.Like(l.EntityType, $"%{term}%")));
        }

        var total = await query.CountAsync(ct);

        var logs = await query
            .OrderByDescending(l => l.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(ct);

        // Los nombres se resuelven aparte para poder incluir empresas ya archivadas,
        // que el filtro global dejaría fuera de un subquery en la proyección.
        var tenantIds = logs.Where(l => l.TenantId is not null).Select(l => l.TenantId!.Value).Distinct().ToList();

        var tenantNames = tenantIds.Count == 0
            ? []
            : await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Where(t => tenantIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return new PagedResult<AuditLogDto>(
            [.. logs.Select(l => l.ToDto(l.TenantId is { } id && tenantNames.TryGetValue(id, out var name) ? name : null))],
            page.Page, page.PageSize, total);
    }
}
