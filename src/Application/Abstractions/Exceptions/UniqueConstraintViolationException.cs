namespace Application.Abstractions.Exceptions;

public sealed class UniqueConstraintViolationException(string? constraint, Exception inner)
    : Exception($"Unique constraint violated: {constraint}", inner);