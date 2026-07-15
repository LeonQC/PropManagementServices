namespace DocumentsService.Business;

/// <summary>A field-level validation error, surfaced in the API error envelope's details[].</summary>
public record FieldError(string Field, string Message);

/// <summary>
/// Outcome of a business operation. Success carries a value; failure carries a
/// machine code (mapped to an HTTP status by the controller) plus a message and
/// optional field-level details. Keeps controllers thin and HTTP-only.
/// </summary>
public class ServiceResult<T>
{
    public bool Succeeded { get; }
    public T? Value { get; }
    public string? Code { get; }
    public string? Message { get; }
    public IReadOnlyList<FieldError> Errors { get; }

    private ServiceResult(bool ok, T? value, string? code, string? message, IReadOnlyList<FieldError>? errors)
    {
        Succeeded = ok;
        Value = value;
        Code = code;
        Message = message;
        Errors = errors ?? [];
    }

    public static ServiceResult<T> Ok(T value) => new(true, value, null, null, null);

    public static ServiceResult<T> Fail(string code, string message, IReadOnlyList<FieldError>? errors = null) =>
        new(false, default, code, message, errors);
}

/// <summary>Stable error codes mapped to HTTP statuses by the controllers.</summary>
public static class ErrorCodes
{
    public const string Validation = "VALIDATION_ERROR";      // 400
    public const string Unauthorized = "INVALID_CREDENTIALS"; // 401
    public const string NotFound = "NOT_FOUND";               // 404
    public const string Conflict = "CONFLICT";                // 409
}
