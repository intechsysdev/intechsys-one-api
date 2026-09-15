using One.Api.Auth;
using One.Api.Extensions;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;
using One.Domain.Enums;
using One.Domain.Identity;

namespace One.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenants")
            .WithTags("Empresas")
            .RequireAuthorization();

        group.MapGet("/", async (
            ITenantService tenants, CurrentUser currentUser,
            int page = 1, int pageSize = 20, string? search = null,
            string? sortBy = null, bool descending = true, TenantStatus? status = null,
            CancellationToken ct = default) =>
        {
            var request = new PageRequest { Page = page, PageSize = pageSize, Search = search, SortBy = sortBy, Descending = descending };

            // La plataforma ve todas las empresas; el resto, solo aquellas a las que pertenece.
            var isPlatformStaff = currentUser.IsPlatformAdmin || currentUser.Roles.Contains(PlatformRoles.PlatformSupport);
            IReadOnlyCollection<Guid>? visible = isPlatformStaff ? null : [.. currentUser.Memberships.Keys];

            return Results.Ok(await tenants.ListAsync(request, status, visible, ct));
        })
        .WithSummary("Lista paginada de empresas visibles para el usuario autenticado.");

        group.MapGet("/{id:guid}", async (Guid id, ITenantService tenants, TenantGuard guard, CancellationToken ct) =>
            guard.CanRead(id)
                ? (await tenants.GetAsync(id, ct)).ToHttpResult()
                : Forbidden())
        .WithSummary("Detalle de una empresa.");

        group.MapGet("/by-slug/{slug}", async (string slug, ITenantService tenants, TenantGuard guard, CancellationToken ct) =>
        {
            var result = await tenants.GetBySlugAsync(slug, ct);
            if (!result.Succeeded) return result.ToHttpResult();

            return guard.CanRead(result.Value!.Id) ? Results.Ok(result.Value) : Forbidden();
        })
        .WithSummary("Detalle de una empresa por su identificador legible.");

        group.MapPost("/", async (CreateTenantRequest request, ITenantService tenants, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;

            var result = await tenants.CreateAsync(request, ct);
            return result.Succeeded
                ? result.ToCreatedResult($"/api/v1/tenants/{result.Value!.Id}")
                : result.ToHttpResult();
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Crea una empresa.");

        group.MapPut("/{id:guid}", async (Guid id, UpdateTenantRequest request, ITenantService tenants, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await tenants.UpdateAsync(id, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithSummary("Actualiza los datos de una empresa.");

        group.MapDelete("/{id:guid}", async (Guid id, ITenantService tenants, CancellationToken ct) =>
            (await tenants.DeleteAsync(id, ct)).ToHttpResult())
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Archiva una empresa y revoca sus credenciales activas.");

        MapMembers(group);
        MapApps(group);
        MapSettings(group);
        MapCredentials(group);

        return app;
    }

    private static void MapMembers(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/members", async (Guid id, ITenantService tenants, TenantGuard guard, CancellationToken ct) =>
            guard.CanRead(id) ? (await tenants.ListMembersAsync(id, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Miembros")
        .WithSummary("Usuarios que pertenecen a la empresa.");

        group.MapPost("/{id:guid}/members", async (Guid id, AddTenantMemberRequest request, ITenantService tenants, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await tenants.AddMemberAsync(id, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Miembros")
        .WithSummary("Añade un usuario existente a la empresa.");

        group.MapPut("/{id:guid}/members/{membershipId:guid}", async (
            Guid id, Guid membershipId, UpdateTenantMemberRequest request,
            ITenantService tenants, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await tenants.UpdateMemberAsync(id, membershipId, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Miembros")
        .WithSummary("Cambia el rol o el estado de un miembro.");

        group.MapDelete("/{id:guid}/members/{membershipId:guid}", async (
            Guid id, Guid membershipId, ITenantService tenants, TenantGuard guard, CancellationToken ct) =>
            guard.CanManage(id) ? (await tenants.RemoveMemberAsync(id, membershipId, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Miembros")
        .WithSummary("Retira a un usuario de la empresa.");
    }

    private static void MapApps(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/apps", async (Guid id, ITenantAppService apps, TenantGuard guard, CancellationToken ct) =>
            guard.CanRead(id) ? (await apps.ListForTenantAsync(id, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Apps")
        .WithSummary("Apps asignadas a la empresa con su estado de configuración.");

        group.MapGet("/{id:guid}/apps/{tenantAppId:guid}", async (
            Guid id, Guid tenantAppId, ITenantAppService apps, TenantGuard guard,
            bool reveal = false, CancellationToken ct = default) =>
        {
            if (!guard.CanRead(id)) return Forbidden();

            var canReveal = reveal && guard.CanRevealSecrets(id);
            return (await apps.GetAsync(id, tenantAppId, canReveal, ct)).ToHttpResult();
        })
        .WithTags("Empresas · Apps")
        .WithSummary("Detalle de la asignación: esquema, variables y credenciales.");

        group.MapPost("/{id:guid}/apps", async (Guid id, AssignAppRequest request, ITenantAppService apps, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await apps.AssignAsync(id, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Apps")
        .WithSummary("Asigna una app del catálogo a la empresa.");

        group.MapPut("/{id:guid}/apps/{tenantAppId:guid}", async (
            Guid id, Guid tenantAppId, UpdateTenantAppRequest request,
            ITenantAppService apps, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await apps.UpdateAsync(id, tenantAppId, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Apps")
        .WithSummary("Actualiza la suscripción de la empresa a una app.");

        group.MapDelete("/{id:guid}/apps/{tenantAppId:guid}", async (
            Guid id, Guid tenantAppId, ITenantAppService apps, TenantGuard guard, CancellationToken ct) =>
            guard.CanManage(id) ? (await apps.RemoveAsync(id, tenantAppId, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Apps")
        .WithSummary("Retira la app de la empresa junto con sus variables y credenciales.");
    }

    private static void MapSettings(RouteGroupBuilder group)
    {
        const string prefix = "/{id:guid}/apps/{tenantAppId:guid}/settings";

        group.MapGet(prefix, async (
            Guid id, Guid tenantAppId, ISettingService settings, TenantGuard guard,
            AppEnvironment? environment = null, bool reveal = false, CancellationToken ct = default) =>
        {
            if (!guard.CanRead(id)) return Forbidden();

            var canReveal = reveal && guard.CanRevealSecrets(id);
            return (await settings.ListAsync(id, tenantAppId, environment, canReveal, ct)).ToHttpResult();
        })
        .WithTags("Empresas · Variables")
        .WithSummary("Variables de configuración por entorno.");

        group.MapPut(prefix, async (
            Guid id, Guid tenantAppId, UpsertSettingRequest request,
            ISettingService settings, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await settings.UpsertAsync(id, tenantAppId, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Variables")
        .WithSummary("Crea o actualiza una variable.");

        group.MapPut($"{prefix}/bulk", async (
            Guid id, Guid tenantAppId, BulkUpsertSettingsRequest request,
            ISettingService settings, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await settings.BulkUpsertAsync(id, tenantAppId, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Variables")
        .WithSummary("Guarda de una vez todas las variables de un entorno.");

        group.MapGet($"{prefix}/{{settingId:guid}}/reveal", async (
            Guid id, Guid tenantAppId, Guid settingId, ISettingService settings, TenantGuard guard, CancellationToken ct) =>
            guard.CanRevealSecrets(id)
                ? (await settings.RevealAsync(id, tenantAppId, settingId, ct)).ToHttpResult(value => Results.Ok(new { value }))
                : Forbidden())
        .WithTags("Empresas · Variables")
        .WithSummary("Descifra y devuelve el valor de una variable secreta. Queda auditado.");

        group.MapDelete($"{prefix}/{{settingId:guid}}", async (
            Guid id, Guid tenantAppId, Guid settingId, ISettingService settings, TenantGuard guard, CancellationToken ct) =>
            guard.CanManage(id) ? (await settings.DeleteAsync(id, tenantAppId, settingId, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Variables")
        .WithSummary("Elimina una variable.");
    }

    private static void MapCredentials(RouteGroupBuilder group)
    {
        const string prefix = "/{id:guid}/apps/{tenantAppId:guid}/credentials";

        group.MapGet(prefix, async (
            Guid id, Guid tenantAppId, ICredentialService credentials, TenantGuard guard, CancellationToken ct) =>
            guard.CanRead(id) ? (await credentials.ListAsync(id, tenantAppId, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Credenciales")
        .WithSummary("Credenciales de integración emitidas, siempre enmascaradas.");

        group.MapPost(prefix, async (
            Guid id, Guid tenantAppId, CreateCredentialRequest request,
            ICredentialService credentials, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await credentials.CreateAsync(id, tenantAppId, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Credenciales")
        .WithSummary("Emite una credencial. Devuelve la api key y el secreto una única vez.");

        group.MapPut($"{prefix}/{{credentialId:guid}}", async (
            Guid id, Guid tenantAppId, Guid credentialId, UpdateCredentialRequest request,
            ICredentialService credentials, TenantGuard guard, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return guard.CanManage(id) ? (await credentials.UpdateAsync(id, tenantAppId, credentialId, request, ct)).ToHttpResult() : Forbidden();
        })
        .WithTags("Empresas · Credenciales")
        .WithSummary("Actualiza nombre, scopes, IPs permitidas o caducidad.");

        group.MapPost($"{prefix}/{{credentialId:guid}}/rotate", async (
            Guid id, Guid tenantAppId, Guid credentialId,
            ICredentialService credentials, TenantGuard guard, CancellationToken ct) =>
            guard.CanManage(id) ? (await credentials.RotateAsync(id, tenantAppId, credentialId, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Credenciales")
        .WithSummary("Genera un nuevo par de claves invalidando el anterior.");

        group.MapPost($"{prefix}/{{credentialId:guid}}/revoke", async (
            Guid id, Guid tenantAppId, Guid credentialId, RevokeCredentialRequest request,
            ICredentialService credentials, TenantGuard guard, CancellationToken ct) =>
            guard.CanManage(id) ? (await credentials.RevokeAsync(id, tenantAppId, credentialId, request, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Credenciales")
        .WithSummary("Revoca la credencial de forma permanente.");

        group.MapDelete($"{prefix}/{{credentialId:guid}}", async (
            Guid id, Guid tenantAppId, Guid credentialId,
            ICredentialService credentials, TenantGuard guard, CancellationToken ct) =>
            guard.CanManage(id) ? (await credentials.DeleteAsync(id, tenantAppId, credentialId, ct)).ToHttpResult() : Forbidden())
        .WithTags("Empresas · Credenciales")
        .WithSummary("Elimina la credencial del registro.");
    }

    private static IResult Forbidden() =>
        Results.Problem(
            detail: "No tiene permisos sobre esta empresa.",
            title: "Acceso denegado",
            statusCode: StatusCodes.Status403Forbidden);
}
