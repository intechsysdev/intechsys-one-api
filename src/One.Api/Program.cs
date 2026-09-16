using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using One.Api.Auth;
using One.Api.Endpoints;
using One.Api.Extensions;
using One.Application.Contracts;
using One.Domain.Identity;
using One.Infrastructure;
using One.Infrastructure.Options;
using One.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ── Identidad de la petición ────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddScoped<TenantGuard>();

// ── Persistencia, Identity y servicios de aplicación ────────────────────────
builder.Services.AddOneInfrastructure(builder.Configuration);

// ── Autenticación JWT ──────────────────────────────────────────────────────
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Falta la sección de configuración Jwt.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };

        // El portal distingue "token caducado" de "token inválido" para decidir si refresca.
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                    context.Response.Headers["X-Token-Expired"] = "true";

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.PlatformAdmin, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(PlatformRoles.PlatformAdmin))
    .AddPolicy(Policies.PlatformRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(PlatformRoles.PlatformAdmin, PlatformRoles.PlatformSupport));

// ── Infraestructura HTTP ───────────────────────────────────────────────────
const string CorsPolicy = "one-front";

// Orígenes exactos del portal. En App Service se sobreescriben con
// Cors__AllowedOrigins__0, Cors__AllowedOrigins__1, ...
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173", "https://localhost:5173"];

// Comodines para los entornos de preview de Static Web Apps, que cambian de host
// en cada pull request (jolly-tree-....<región>.6.azurestaticapps.net).
var allowedOriginPatterns = builder.Configuration.GetSection("Cors:AllowedOriginPatterns").Get<string[]>() ?? [];

var corsOrigins = allowedOrigins
    .Concat(allowedOriginPatterns)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(corsOrigins)
    .SetIsOriginAllowedToAllowWildcardSubdomains()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("X-Config-Version", "X-Token-Expired")
    .AllowCredentials()));

builder.Services.AddOneRateLimiting();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks().AddDbContextCheck<OneDbContext>("sql-server");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Los enums viajan como texto para que el portal no dependa de valores numéricos.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "One · Centralizador de configuración",
            Version = "v1",
            Description = "API multitenant para administrar empresas, aplicaciones, credenciales y variables de integración."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token emitido por /api/v1/auth/login."
        };
        document.Components.SecuritySchemes["apiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = "X-Api-Key",
            In = ParameterLocation.Header,
            Description = "Api key de integración. Se acompaña de la cabecera X-Api-Secret."
        };

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging(options => options.GetLevel = (http, _, ex) =>
    ex is not null || http.Response.StatusCode >= 500
        ? Serilog.Events.LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/health")
            ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information);

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("One · API")
    .WithTheme(ScalarTheme.DeepSpace)
    .WithDarkMode(true));

app.MapHealthChecks("/health").AllowAnonymous();

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapAppEndpoints();
app.MapUserEndpoints();
app.MapDashboardEndpoints();
app.MapIntegrationEndpoints();

// Migraciones y datos iniciales antes de aceptar tráfico: se ejecutan en cada
// arranque, de modo que cada publicación deja la base al día automáticamente.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("One.Startup");

    try
    {
        startupLogger.LogInformation("Aplicando migraciones pendientes y datos iniciales…");

        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.RunAsync();

        startupLogger.LogInformation("Base de datos lista.");
    }
    catch (Exception ex)
    {
        // Sin base no hay API: se deja rastro explícito en el log de App Service y se aborta
        // el arranque para que el despliegue falle en vez de servir tráfico roto.
        startupLogger.LogCritical(ex, "No se pudo preparar la base de datos. Se aborta el arranque.");
        throw;
    }
}

app.Run();
