namespace One.Application.Common;

/// <summary>Clasificación del fallo, para traducirlo al código HTTP correcto en la capa de API.</summary>
public enum ErrorKind
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5
}

/// <summary>Resultado de una operación de aplicación, sin excepciones para el flujo esperado.</summary>
public class OperationResult
{
    protected OperationResult(bool succeeded, ErrorKind kind, string? message, IReadOnlyDictionary<string, string[]>? errors)
    {
        Succeeded = succeeded;
        Kind = kind;
        Message = message;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public bool Succeeded { get; }
    public ErrorKind Kind { get; }
    public string? Message { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static OperationResult Ok() => new(true, ErrorKind.None, null, null);

    public static OperationResult Fail(ErrorKind kind, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        => new(false, kind, message, errors);

    public static OperationResult NotFound(string message = "No se encontró el recurso solicitado.")
        => Fail(ErrorKind.NotFound, message);

    public static OperationResult Conflict(string message) => Fail(ErrorKind.Conflict, message);

    public static OperationResult Invalid(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        => Fail(ErrorKind.Validation, message, errors);

    public static OperationResult Forbidden(string message = "No tiene permisos sobre este recurso.")
        => Fail(ErrorKind.Forbidden, message);

    public static OperationResult Unauthorized(string message = "Credenciales inválidas.")
        => Fail(ErrorKind.Unauthorized, message);
}

/// <summary>Resultado de una operación que devuelve un valor.</summary>
public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool succeeded, T? value, ErrorKind kind, string? message, IReadOnlyDictionary<string, string[]>? errors)
        : base(succeeded, kind, message, errors) => Value = value;

    public T? Value { get; }

    public static OperationResult<T> Ok(T value) => new(true, value, ErrorKind.None, null, null);

    public static new OperationResult<T> Fail(ErrorKind kind, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        => new(false, default, kind, message, errors);

    public static new OperationResult<T> NotFound(string message = "No se encontró el recurso solicitado.")
        => Fail(ErrorKind.NotFound, message);

    public static new OperationResult<T> Conflict(string message) => Fail(ErrorKind.Conflict, message);

    public static new OperationResult<T> Invalid(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        => Fail(ErrorKind.Validation, message, errors);

    public static new OperationResult<T> Forbidden(string message = "No tiene permisos sobre este recurso.")
        => Fail(ErrorKind.Forbidden, message);

    public static new OperationResult<T> Unauthorized(string message = "Credenciales inválidas.")
        => Fail(ErrorKind.Unauthorized, message);
}
