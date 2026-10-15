using Domain.Abstractions;

namespace Api.Extensions;

[ExcludeFromDescription]
public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);
    }

    public static IResult ToHttpResult<T>(this Result<IEnumerable<T>> result)
    {
        if (!result.IsSuccess)
            return Problem(result.Error);

        if (result.Value is ICollection<T> collection)
        {
            return collection.Count == 0 ? Results.NoContent() : Results.Ok(collection);
        }

        // só materializa se realmente precisar (IQueryable, iterator, etc)
        var list = result.Value.ToList();
        return list.Count == 0 ? Results.NoContent() : Results.Ok(list);
    }

    /// <summary>Name of the ProblemDetails extension carrying the stable, machine-readable error code.</summary>
    public const string CodeExtension = "code";

    // RFC 9457: "title" is the human-readable summary of the problem *type* (the framework default
    // for the status), "detail" is this occurrence's message, and the stable error code travels
    // in the "code" extension member so clients never parse the title.
    private static IResult Problem(Error error)
    {
        var extensions = new Dictionary<string, object?> { [CodeExtension] = error.Code };

        if (error is ValidationError ve)
            return Results.ValidationProblem(
                ve.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()),
                detail: ve.Message,
                extensions: extensions);

        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(statusCode: statusCode, detail: error.Message, extensions: extensions);
    }
}