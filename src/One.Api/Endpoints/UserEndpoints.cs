using One.Api.Auth;
using One.Api.Extensions;
using One.Application.Common;
using One.Application.Contracts;
using One.Application.Dtos;

namespace One.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Usuarios")
            .RequireAuthorization(Policies.PlatformRead);

        group.MapGet("/", async (
            IUserService users,
            int page = 1, int pageSize = 20, string? search = null,
            string? sortBy = null, bool descending = true,
            Guid? tenantId = null, bool? isActive = null, string? role = null,
            CancellationToken ct = default) =>
        {
            var request = new PageRequest { Page = page, PageSize = pageSize, Search = search, SortBy = sortBy, Descending = descending };
            return Results.Ok(await users.ListAsync(request, tenantId, isActive, role, ct));
        })
        .WithSummary("Lista paginada de usuarios.");

        group.MapGet("/roles", async (IUserService users, CancellationToken ct) =>
            Results.Ok(await users.ListRolesAsync(ct)))
        .WithSummary("Roles de plataforma disponibles.");

        group.MapGet("/{id:guid}", async (Guid id, IUserService users, CancellationToken ct) =>
            (await users.GetAsync(id, ct)).ToHttpResult())
        .WithSummary("Detalle de un usuario con sus roles y empresas.");

        group.MapPost("/", async (CreateUserRequest request, IUserService users, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;

            var result = await users.CreateAsync(request, ct);
            if (!result.Succeeded) return result.ToHttpResult();

            var (user, temporaryPassword) = result.Value;
            return Results.Created($"/api/v1/users/{user.Id}", new { user, temporaryPassword });
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Crea un usuario. Si no se envía contraseña, se genera una temporal.");

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, IUserService users, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return (await users.UpdateAsync(id, request, ct)).ToHttpResult();
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Actualiza los datos y roles de un usuario.");

        group.MapDelete("/{id:guid}", async (Guid id, IUserService users, CancellationToken ct) =>
            (await users.DeleteAsync(id, ct)).ToHttpResult())
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Elimina un usuario que no sea el único propietario de una empresa.");

        group.MapPost("/{id:guid}/reset-password", async (
            Guid id, ResetPasswordRequest request, IUserService users, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return (await users.ResetPasswordAsync(id, request, ct)).ToHttpResult();
        })
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Restablece la contraseña y cierra las sesiones del usuario.");

        group.MapPost("/{id:guid}/lock", async (Guid id, IUserService users, CancellationToken ct) =>
            (await users.SetLockoutAsync(id, locked: true, ct)).ToHttpResult())
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Bloquea el acceso del usuario.");

        group.MapPost("/{id:guid}/unlock", async (Guid id, IUserService users, CancellationToken ct) =>
            (await users.SetLockoutAsync(id, locked: false, ct)).ToHttpResult())
        .RequireAuthorization(Policies.PlatformAdmin)
        .WithSummary("Levanta el bloqueo del usuario.");

        return app;
    }
}
