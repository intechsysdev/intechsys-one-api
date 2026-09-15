using Microsoft.EntityFrameworkCore;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Entities;
using One.Infrastructure.Persistence;
using One.Infrastructure.Services.Internal;

namespace One.Infrastructure.Services;

/// <summary>Catálogo global de apps integrables y el esquema de variables que cada una declara.</summary>
public sealed class AppService(OneDbContext db, IAuditService audit, ICurrentUser currentUser) : IAppService
{
    public async Task<PagedResult<AppDto>> ListAsync(PageRequest page, bool? isActive, string? category, CancellationToken ct = default)
    {
        var query = db.Apps.AsNoTracking();

        if (isActive is not null)
            query = query.Where(a => a.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(a => a.Category == category);

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var term = page.Search.Trim();
            query = query.Where(a =>
                EF.Functions.Like(a.Name, $"%{term}%") ||
                EF.Functions.Like(a.Slug, $"%{term}%") ||
                (a.Description != null && EF.Functions.Like(a.Description, $"%{term}%")));
        }

        query = (page.SortBy?.ToLowerInvariant(), page.Descending) switch
        {
            ("name", true) => query.OrderByDescending(a => a.Name),
            ("name", false) => query.OrderBy(a => a.Name),
            ("category", true) => query.OrderByDescending(a => a.Category).ThenBy(a => a.Name),
            ("category", false) => query.OrderBy(a => a.Category).ThenBy(a => a.Name),
            ("createdat", false) => query.OrderBy(a => a.CreatedAt),
            _ => query.OrderByDescending(a => a.CreatedAt)
        };

        var total = await query.CountAsync(ct);

        var rows = await query
            .Skip(page.Skip).Take(page.PageSize)
            .Select(a => new
            {
                App = a,
                SettingCount = a.SettingDefinitions.Count(),
                TenantCount = a.Subscriptions.Count()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => r.App.ToDto(r.SettingCount, r.TenantCount)).ToList();

        return new PagedResult<AppDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<OperationResult<AppDetailDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await LoadDetailAsync(id, ct);
        return detail is null
            ? OperationResult<AppDetailDto>.NotFound("La aplicación no existe.")
            : OperationResult<AppDetailDto>.Ok(detail);
    }

    public async Task<OperationResult<AppDetailDto>> CreateAsync(CreateAppRequest request, CancellationToken ct = default)
    {
        var requestedSlug = request.Slug?.Trim();
        var baseSlug = string.IsNullOrWhiteSpace(requestedSlug) ? Slugger.From(request.Name) : requestedSlug;

        if (string.IsNullOrWhiteSpace(baseSlug))
            return OperationResult<AppDetailDto>.Invalid("No se pudo derivar un identificador válido del nombre.");

        var taken = await db.Apps.IgnoreQueryFilters()
            .Where(a => a.Slug.StartsWith(baseSlug))
            .Select(a => a.Slug)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(requestedSlug) && taken.Contains(baseSlug, StringComparer.OrdinalIgnoreCase))
            return OperationResult<AppDetailDto>.Conflict($"Ya existe una aplicación con el identificador {requestedSlug}.");

        var duplicatedKey = request.SettingDefinitions
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicatedKey is not null)
            return OperationResult<AppDetailDto>.Invalid($"La variable {duplicatedKey.Key} está declarada más de una vez.");

        var app = new AppDefinition
        {
            Name = request.Name.Trim(),
            Slug = Slugger.MakeUnique(baseSlug, candidate => taken.Contains(candidate, StringComparer.OrdinalIgnoreCase)),
            Description = request.Description?.Trim(),
            Category = request.Category?.Trim(),
            IconUrl = request.IconUrl?.Trim(),
            Color = request.Color?.Trim(),
            Version = request.Version?.Trim(),
            HomepageUrl = request.HomepageUrl?.Trim(),
            DocumentationUrl = request.DocumentationUrl?.Trim(),
            SupportEmail = request.SupportEmail?.Trim(),
            IsActive = request.IsActive,
            IsPublic = request.IsPublic,
            AvailableScopes = request.AvailableScopes?.Trim(),
            CreatedBy = currentUser.UserId
        };

        var order = 0;
        foreach (var definition in request.SettingDefinitions)
            app.SettingDefinitions.Add(BuildDefinition(app.Id, definition, order++));

        db.Apps.Add(app);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("app.created", nameof(AppDefinition), app.Id.ToString(), metadata:
            new { app.Name, app.Slug, Settings = app.SettingDefinitions.Count }, ct: ct);

        return OperationResult<AppDetailDto>.Ok((await LoadDetailAsync(app.Id, ct))!);
    }

    public async Task<OperationResult<AppDetailDto>> UpdateAsync(Guid id, UpdateAppRequest request, CancellationToken ct = default)
    {
        var app = await db.Apps.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return OperationResult<AppDetailDto>.NotFound("La aplicación no existe.");

        app.Name = request.Name.Trim();
        app.Description = request.Description?.Trim();
        app.Category = request.Category?.Trim();
        app.IconUrl = request.IconUrl?.Trim();
        app.Color = request.Color?.Trim();
        app.Version = request.Version?.Trim();
        app.HomepageUrl = request.HomepageUrl?.Trim();
        app.DocumentationUrl = request.DocumentationUrl?.Trim();
        app.SupportEmail = request.SupportEmail?.Trim();
        app.IsActive = request.IsActive;
        app.IsPublic = request.IsPublic;
        app.AvailableScopes = request.AvailableScopes?.Trim();
        app.UpdatedAt = DateTimeOffset.UtcNow;
        app.UpdatedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("app.updated", nameof(AppDefinition), app.Id.ToString(), metadata:
            new { app.Name, app.IsActive }, ct: ct);

        return OperationResult<AppDetailDto>.Ok((await LoadDetailAsync(app.Id, ct))!);
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var app = await db.Apps.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return OperationResult.NotFound("La aplicación no existe.");

        var subscribers = await db.TenantApps.CountAsync(s => s.AppId == id, ct);
        if (subscribers > 0)
            return OperationResult.Conflict($"No se puede eliminar: {subscribers} empresa(s) tienen la app asignada. Desactívela o retire las asignaciones primero.");

        app.IsDeleted = true;
        app.DeletedAt = DateTimeOffset.UtcNow;
        app.DeletedBy = currentUser.UserId;
        app.IsActive = false;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("app.deleted", nameof(AppDefinition), id.ToString(), metadata: new { app.Slug }, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<AppSettingDefinitionDto>> UpsertSettingDefinitionAsync(Guid appId, UpsertSettingDefinitionRequest request, CancellationToken ct = default)
    {
        if (!await db.Apps.AnyAsync(a => a.Id == appId, ct))
            return OperationResult<AppSettingDefinitionDto>.NotFound("La aplicación no existe.");

        var key = request.Key.Trim();

        var clash = await db.AppSettingDefinitions
            .AnyAsync(d => d.AppId == appId && d.Key == key && (request.Id == null || d.Id != request.Id), ct);

        if (clash)
            return OperationResult<AppSettingDefinitionDto>.Conflict($"La aplicación ya declara la variable {key}.");

        AppSettingDefinition definition;

        if (request.Id is { } definitionId)
        {
            var existing = await db.AppSettingDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId && d.AppId == appId, ct);
            if (existing is null) return OperationResult<AppSettingDefinitionDto>.NotFound("La variable no existe.");

            Apply(existing, request);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = currentUser.UserId;
            definition = existing;
        }
        else
        {
            var nextOrder = await db.AppSettingDefinitions.Where(d => d.AppId == appId)
                .Select(d => (int?)d.DisplayOrder).MaxAsync(ct) ?? -1;

            definition = BuildDefinition(appId, request, request.DisplayOrder > 0 ? request.DisplayOrder : nextOrder + 1);
            db.AppSettingDefinitions.Add(definition);
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("app.setting_definition_saved", nameof(AppSettingDefinition), definition.Id.ToString(),
            metadata: new { appId, definition.Key, definition.IsSecret }, ct: ct);

        return OperationResult<AppSettingDefinitionDto>.Ok(definition.ToDto());
    }

    public async Task<OperationResult> DeleteSettingDefinitionAsync(Guid appId, Guid definitionId, CancellationToken ct = default)
    {
        var definition = await db.AppSettingDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId && d.AppId == appId, ct);
        if (definition is null) return OperationResult.NotFound("La variable no existe.");

        db.AppSettingDefinitions.Remove(definition);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("app.setting_definition_deleted", nameof(AppSettingDefinition), definitionId.ToString(),
            metadata: new { appId, definition.Key }, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct = default) =>
        await db.Apps.AsNoTracking()
            .Where(a => a.Category != null && a.Category != "")
            .Select(a => a.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

    private async Task<AppDetailDto?> LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Apps.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                App = a,
                SettingCount = a.SettingDefinitions.Count(),
                TenantCount = a.Subscriptions.Count(),
                Definitions = a.SettingDefinitions.OrderBy(d => d.DisplayOrder).ThenBy(d => d.Key).ToList()
            })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new AppDetailDto(
                row.App.ToDto(row.SettingCount, row.TenantCount),
                [.. row.Definitions.Select(d => d.ToDto())]);
    }

    private static AppSettingDefinition BuildDefinition(Guid appId, UpsertSettingDefinitionRequest request, int displayOrder)
    {
        var definition = new AppSettingDefinition { AppId = appId, DisplayOrder = displayOrder };
        Apply(definition, request);
        return definition;
    }

    private static void Apply(AppSettingDefinition definition, UpsertSettingDefinitionRequest request)
    {
        definition.Key = request.Key.Trim();
        definition.Label = request.Label.Trim();
        definition.Description = request.Description?.Trim();
        definition.Placeholder = request.Placeholder?.Trim();
        definition.DataType = Mappings.ResolveDataType(request.DataType, request.IsSecret);
        definition.IsRequired = request.IsRequired;
        definition.IsSecret = request.IsSecret;
        definition.DefaultValue = request.IsSecret ? null : request.DefaultValue?.Trim();
        definition.ValidationRegex = request.ValidationRegex?.Trim();
        definition.AllowedValues = request.AllowedValues?.Trim();
        definition.Group = request.Group?.Trim();

        if (request.DisplayOrder > 0)
            definition.DisplayOrder = request.DisplayOrder;
    }
}
