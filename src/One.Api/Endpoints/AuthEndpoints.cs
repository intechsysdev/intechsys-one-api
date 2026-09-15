using One.Api.Auth;
using One.Api.Extensions;
using One.Application.Contracts;
using One.Application.Dtos;

namespace One.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Autenticación");

        group.MapPost("/login", async (LoginRequest request, IAuthService auth, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return (await auth.LoginAsync(request, ct)).ToHttpResult();
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.Auth)
        .WithSummary("Inicia sesión y devuelve el token de acceso y el de refresco.");

        group.MapPost("/refresh", async (RefreshRequest request, IAuthService auth, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return (await auth.RefreshAsync(request, ct)).ToHttpResult();
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.Auth)
        .WithSummary("Rota el token de refresco y emite un nuevo token de acceso.");

        group.MapPost("/logout", async (RefreshRequest request, IAuthService auth, CancellationToken ct) =>
            (await auth.LogoutAsync(request.RefreshToken, ct)).ToHttpResult())
        .RequireAuthorization()
        .WithSummary("Revoca el token de refresco de la sesión actual.");

        group.MapGet("/me", async (CurrentUser user, IAuthService auth, CancellationToken ct) =>
            user.UserId is { } id
                ? (await auth.GetCurrentUserAsync(id, ct)).ToHttpResult()
                : Results.Unauthorized())
        .RequireAuthorization()
        .WithSummary("Devuelve el perfil, los roles y las empresas del usuario autenticado.");

        group.MapPut("/me", async (UpdateProfileRequest request, CurrentUser user, IAuthService auth, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return user.UserId is { } id
                ? (await auth.UpdateProfileAsync(id, request, ct)).ToHttpResult()
                : Results.Unauthorized();
        })
        .RequireAuthorization()
        .WithSummary("Actualiza el perfil propio.");

        group.MapPost("/change-password", async (ChangePasswordRequest request, CurrentUser user, IAuthService auth, CancellationToken ct) =>
        {
            if (!request.TryValidate(out var problem)) return problem!;
            return user.UserId is { } id
                ? (await auth.ChangePasswordAsync(id, request, ct)).ToHttpResult()
                : Results.Unauthorized();
        })
        .RequireAuthorization()
        .WithSummary("Cambia la contraseña propia y cierra el resto de sesiones.");

        return app;
    }
}
