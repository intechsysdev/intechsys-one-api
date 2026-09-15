using System.Threading.RateLimiting;

namespace One.Api.Extensions;

/// <summary>Nombres de las políticas de limitación de tasa.</summary>
public static class RateLimitPolicies
{
    /// <summary>Protege login y refresh de ataques de fuerza bruta.</summary>
    public const string Auth = "auth";

    /// <summary>Acota el consumo de las apps integradas por credencial.</summary>
    public const string Integration = "integration";
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddOneRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Auth, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Se particiona por api key para que una app ruidosa no afecte al resto.
            options.AddPolicy(RateLimitPolicies.Integration, http =>
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: http.Request.Headers["X-Api-Key"].FirstOrDefault()
                                  ?? http.Connection.RemoteIpAddress?.ToString()
                                  ?? "desconocida",
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 120,
                        TokensPerPeriod = 60,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "Demasiadas solicitudes",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Superó el límite de peticiones. Inténtelo de nuevo en un minuto."
                }, ct);
            };
        });
}
