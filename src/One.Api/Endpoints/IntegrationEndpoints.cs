using One.Api.Auth;
using One.Api.Extensions;
using One.Application.Common;
using One.Application.Contracts;

namespace One.Api.Endpoints;

/// <summary>
/// Superficie que consumen las apps integradas. No usa JWT: se autentica con el par
/// api key / secreto emitido para la empresa desde el portal.
/// </summary>
public static class IntegrationEndpoints
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApiSecretHeader = "X-Api-Secret";

    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/integration")
            .WithTags("Integración de apps")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Integration);

        group.MapGet("/config", async (HttpContext http, IIntegrationService integration, CancellationToken ct) =>
        {
            var (key, secret) = ReadCredentials(http);
            var result = await integration.ResolveConfigurationAsync(key, secret, ClientIp(http), ct);

            if (result.Succeeded)
                http.Response.Headers["X-Config-Version"] = result.Value!.ConfigVersion;

            return result.ToHttpResult();
        })
        .WithSummary("Devuelve la configuración completa de la app para la empresa dueña de la credencial.")
        .WithDescription($"Requiere las cabeceras {ApiKeyHeader} y {ApiSecretHeader}. La respuesta incluye " +
                         "X-Config-Version para que la app pueda cachear y revalidar.");

        group.MapGet("/verify", async (HttpContext http, IIntegrationService integration, CancellationToken ct) =>
        {
            var (key, secret) = ReadCredentials(http);
            return (await integration.IntrospectAsync(key, secret, ClientIp(http), ct)).ToHttpResult();
        })
        .WithSummary("Comprobación ligera de la credencial sin devolver variables.");

        return app;
    }

    private static (string Key, string Secret) ReadCredentials(HttpContext http)
    {
        var key = http.Request.Headers[ApiKeyHeader].FirstOrDefault() ?? string.Empty;
        var secret = http.Request.Headers[ApiSecretHeader].FirstOrDefault() ?? string.Empty;

        // Alternativa Basic para clientes que no pueden fijar cabeceras propias.
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(secret))
        {
            var authorization = http.Request.Headers.Authorization.FirstOrDefault();

            if (authorization?.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..].Trim()));
                    var separator = decoded.IndexOf(':');

                    if (separator > 0)
                        return (decoded[..separator], decoded[(separator + 1)..]);
                }
                catch (FormatException)
                {
                    // Cabecera malformada: se trata como ausencia de credenciales.
                }
            }
        }

        return (key, secret);
    }

    private static string? ClientIp(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        return string.IsNullOrWhiteSpace(forwarded)
            ? http.Connection.RemoteIpAddress?.ToString()
            : forwarded.Split(',')[0].Trim();
    }
}

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Panel")
            .RequireAuthorization(Policies.PlatformRead);

        group.MapGet("/dashboard", async (IDashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetStatsAsync(ct)))
        .WithSummary("Métricas agregadas y actividad reciente de la plataforma.");

        group.MapGet("/audit-logs", async (
            IDashboardService dashboard,
            int page = 1, int pageSize = 25, string? search = null,
            Guid? tenantId = null, string? action = null,
            CancellationToken ct = default) =>
        {
            var request = new PageRequest { Page = page, PageSize = pageSize, Search = search };
            return Results.Ok(await dashboard.ListAuditLogsAsync(request, tenantId, action, ct));
        })
        .WithSummary("Traza de auditoría paginada.");

        return app;
    }
}
