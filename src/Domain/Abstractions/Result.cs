using System.Diagnostics.CodeAnalysis;
namespace Domain.Abstractions;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        switch (isSuccess)
        {
            case true when error != Error.None:
                throw new InvalidOperationException();
            case false when error == Error.None:
                throw new InvalidOperationException();
            default:
                IsSuccess = isSuccess;
                Error = error;
                break;
        }
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);

    public static Result<T> Create<T>(T? value) =>
        value is not null ? Success(value) : Failure<T>(Error.NullValue);

    public static implicit operator Result(Error error) => Failure(error);
}

public class Result<T> : Result
{
    private readonly T? _value;
    protected internal Result(T? value, bool isSuccess, Error error) : base(isSuccess, error)
        => _value = value;

    [NotNull]
    public T Value => _value! ?? throw new InvalidOperationException("Result has no value");

    public static implicit operator Result<T>(T? value) => Create(value);
    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}

public record Error(string Code, string Message, ErrorType Type)
{
    public static Error None => new(string.Empty, string.Empty, ErrorType.Failure);
    public static Error NullValue => new("Error.NullValue", "Um valor nulo foi fornecido.", ErrorType.Failure);
    public static Error InvalidOperation => new("Error.InvalidOperation", "A operação solicitada é inválida.", ErrorType.Failure);
    public static Error NotFound(string entity) => new("Error.NotFound", $"{entity} não encontrado.", ErrorType.NotFound);
    public static Error Conflict(string entity) => new("Error.Conflict", $"{entity} já existe.", ErrorType.Conflict);
    public static Error Validation(string message) => new("Error.Validation", message, ErrorType.Validation);
    public static Error Unauthorized(string message = "Não autorizado.") => new("Error.Unauthorized", message, ErrorType.Unauthorized);
    public static Error Forbidden(string message = "Acesso negado.") => new("Error.Forbidden", message, ErrorType.Forbidden);

    public override string ToString() => $"{Code} : {Message}";
}

public sealed record ValidationError : Error
{
    public ValidationError(Error[] errors)
        : base("Error.Validation", "Um ou mais erros de validação ocorreram.", ErrorType.Validation)
        => Errors = errors;

    public Error[] Errors { get; }

    public static ValidationError FromResults(IEnumerable<Result> results) =>
        new([.. results.Where(r => r.IsFailure).Select(r => r.Error)]);
}

public enum ErrorType
{
    Failure,       // 500
    Validation,    // 400
    NotFound,      // 404
    Conflict,      // 409
    Unauthorized,  // 401
    Forbidden      // 403
}