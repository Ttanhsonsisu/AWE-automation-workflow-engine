using System.Text.Json.Serialization;

namespace AWE.Shared.Primitives;

public sealed class Error
{
    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }
    public Dictionary<string, object>? Metadata { get; private set; }

    private Error(string code, string message, ErrorType type)
    {
        Code = code;
        Message = message;
        Type = type;
    }

    // Factory methods
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error Unexpected(string code, string message) => new(code, message, ErrorType.Unexpected);
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    // Fluent methods
    public Error WithMetadata(string key, object value)
    {
        Metadata ??= new Dictionary<string, object>();
        Metadata[key] = value;
        return this;
    }

    public Error WithMetadata(Dictionary<string, object> metadata)
    {
        Metadata = metadata;
        return this;
    }

    // Common errors
    public static readonly Error Required = Validation("Validation.Required", "Field is required");
    public static readonly Error InvalidFormat = Validation("Validation.InvalidFormat", "Invalid format");
    public static readonly Error NotFoundEntity = NotFound("NotFound.Entity", "Entity not found");
    public static readonly Error AlreadyExists = Conflict("Conflict.AlreadyExists", "Already exists");
    public static readonly Error Unauthenticated = Unauthorized("Auth.Unauthenticated", "Authentication required");
    public static readonly Error UnexpectedError = Unexpected("System.Unexpected", "Unexpected error occurred");

    // Conversion
    public override string ToString() => $"{Code}: {Message}";
    public Exception ToException() => new DomainException(Message);
}

public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
    Failure = 6,
    Unexpected = 7,
    BusinessRule = 8
}

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception inner) : base(message, inner) { }
}
