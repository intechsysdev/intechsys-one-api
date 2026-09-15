using System.Text.Json;
using Microsoft.Extensions.Logging;
using One.Application.Contracts;
using One.Domain.Entities;
using One.Infrastructure.Persistence;

namespace One.Infrastructure.Services;

/// <summary>
/// Escribe la traza de auditoría. Un fallo al auditar nunca debe tumbar la operación
/// de negocio: se registra en el log y se continúa.
/// </summary>
public sealed class AuditService(
    OneDbContext db,
    ICurrentUser currentUser,
    ILogger<AuditService> logger) : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task LogAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        Guid? tenantId = null,
        object? metadata = null,
        bool success = true,
        string? errorMessage = null,
        string? actorName = null,
        Guid? actorId = null,
        CancellationToken ct = default)
    {
        try
        {
            var payload = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions);

            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                UserId = actorId ?? currentUser.UserId,
                ActorName = actorName ?? currentUser.DisplayName ?? currentUser.Email,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Metadata = payload is { Length: > 4000 } ? payload[..4000] : payload,
                IpAddress = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent is { Length: > 400 } ua ? ua[..400] : currentUser.UserAgent,
                Success = success,
                ErrorMessage = errorMessage is { Length: > 1000 } msg ? msg[..1000] : errorMessage
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo registrar la auditoría de la acción {Action}", action);
        }
    }
}
