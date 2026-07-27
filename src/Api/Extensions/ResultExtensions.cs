using Domain.Abstractions;

namespace Api.Extensions;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.IsSuccess ? Results.Ok() : Problem(result.Error);
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

    private static IResult Problem(Error error)
    {
        if (error is ValidationError ve)
            return Results.ValidationProblem(
                ve.Errors.ToDictionary(e => e.Code, e => new[] { e.Message }));

        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(statusCode: statusCode, title: error.Code, detail: error.Message);
    }
}