using One.Api.Auth;
using One.Api.Extensions;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;

namespace One.Api.Endpoints;

public static class AppEndpoints
{
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/apps")
            .WithTags("Catálogo de apps")
            .RequireAuthorization();

        group.MapGet("/", async (
            IAppService apps,
            int page = 1, int pageSize = 20, string? search = null,
            string? sortBy = null, bool descending = true,
            bool? isActive = null, string? category = null,
            CancellationToken ct = default) =>
        {
            var request = new PageRequest { Page = page, PageSize = pageSize, Search = search, SortBy = sortBy, Descending = descending };
            return Results.Ok(await apps.ListAsync(request, isActive, category, ct));
        })
        .WithSummary("Lista paginada del catálogo de aplicaciones.");

        group.MapGet("/categories", async (IAppService apps, CancellationToken ct) =>
            Results.Ok(await apps.ListCategoriesAsync(ct)))
        .WithSummary("Categorías en uso, para los filtros del portal.");

        group.MapGet("/{id:guid}", async (Guid id, IAppService apps, CancellationToken ct) =>
            (await apps.GetAsync(id, ct)).ToHttpResult())
        .WithSummary("Detalle de una app con el esquema de variables que declara.");

        group.MapPost("/", async (CreateAppRequest request, IAppService apps, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;

            foreach (var definition in request.SettingDefinitions)
                if (!definition.TryValidate(out var definitionProblem)) return definitionProblem!;

            var result = await apps.CreateAsync(request, ct);
            return result.Succeeded
                ? result.ToCreatedResult($"/api/v1/apps/{result.Value!.App.Id}")
                : result.ToHttpResult();
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Registra una app en el catálogo.");

        group.MapPut("/{id:guid}", async (Guid id, UpdateAppRequest request, IAppService apps, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return (await apps.UpdateAsync(id, request, ct)).ToHttpResult();
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Actualiza los datos de una app del catálogo.");

        group.MapDelete("/{id:guid}", async (Guid id, IAppService apps, CancellationToken ct) =>
            (await apps.DeleteAsync(id, ct)).ToHttpResult())
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Elimina una app que no tenga empresas asignadas.");

        group.MapPut("/{id:guid}/settings-schema", async (
            Guid id, UpsertSettingDefinitionRequest request, IAppService apps, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return (await apps.UpsertSettingDefinitionAsync(id, request, ct)).ToHttpResult();
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Crea o actualiza una variable del esquema de la app.");

        group.MapDelete("/{id:guid}/settings-schema/{definitionId:guid}", async (
            Guid id, Guid definitionId, IAppService apps, CancellationToken ct) =>
            (await apps.DeleteSettingDefinitionAsync(id, definitionId, ct)).ToHttpResult())
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Elimina una variable del esquema de la app.");

        return app;
    }
}
