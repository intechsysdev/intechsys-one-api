using Microsoft.AspNetCore.Mvc;
using One.Application.Common;

namespace One.Api.Extensions;

/// <summary>Traduce el resultado de aplicación al código HTTP y al ProblemDetails correspondiente.</summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this OperationResult<T> result, Func<T, IResult>? onSuccess = null) =>
        result.Succeeded
            ? onSuccess?.Invoke(result.Value!) ?? Results.Ok(result.Value)
            : Problem(result);

    public static IResult ToHttpResult(this OperationResult result, Func<IResult>? onSuccess = null) =>
        result.Succeeded
            ? onSuccess?.Invoke() ?? Results.NoContent()
            : Problem(result);

    public static IResult ToCreatedResult<T>(this OperationResult<T> result, string location) =>
        result.Succeeded ? Results.Created(location, result.Value) : Problem(result);

    private static IResult Problem(OperationResult result)
    {
        var status = result.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var title = result.Kind switch
        {
            ErrorKind.Validation => "Datos inválidos",
            ErrorKind.Unauthorized => "No autenticado",
            ErrorKind.Forbidden => "Acceso denegado",
            ErrorKind.NotFound => "No encontrado",
            ErrorKind.Conflict => "Conflicto",
            _ => "Solicitud incorrecta"
        };

        if (result.Errors.Count > 0)
        {
            return Results.ValidationProblem(
                result.Errors.ToDictionary(e => e.Key, e => e.Value),
                detail: result.Message,
                title: title,
                statusCode: status);
        }

        return Results.Problem(detail: result.Message, title: title, statusCode: status);
    }

    /// <summary>Valida un DTO con sus DataAnnotations antes de llegar al servicio.</summary>
    public static bool TryValidate<T>(this T model, out IResult? problem)
    {
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(model!);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        if (System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model!, context, results, validateAllProperties: true))
        {
            problem = null;
            return true;
        }

        var errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(string.Empty), (r, member) => (Member: member, r.ErrorMessage))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Valor inválido").ToArray());

        problem = Results.ValidationProblem(errors, title: "Datos inválidos", statusCode: StatusCodes.Status400BadRequest);
        return false;
    }
}

/// <summary>Traduce una excepción no controlada a ProblemDetails sin filtrar detalles internos.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        // Un cuerpo mal formado es culpa del cliente: devolver 500 lo llevaría a reintentar
        // algo que nunca va a funcionar.
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogWarning(exception, "Solicitud mal formada en {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Solicitud incorrecta",
                Status = StatusCodes.Status400BadRequest,
                Detail = environment.IsDevelopment() ? badRequest.Message : "El cuerpo de la solicitud no es válido.",
                Instance = context.Request.Path
            }, ct);

            return true;
        }

        logger.LogError(exception, "Error no controlado en {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Error interno",
            Status = StatusCodes.Status500InternalServerError,
            Detail = environment.IsDevelopment() ? exception.ToString() : "Ocurrió un error procesando la solicitud.",
            Instance = context.Request.Path
        }, ct);

        return true;
    }
}
