using Api.Extensions;
using Application.Abstractions.Services;
using Domain.Abstractions;

namespace Api.Endpoints;

internal static class SharedBillsEndpoint
{
    public static RouteGroupBuilder MapSharedBillsEndpoint(this RouteGroupBuilder groupBuilder)
    {
        var group = groupBuilder
        .MapGroup("/bills/shared")
        .AllowAnonymous();

        group.MapGet("", GetSharedBills);

        return groupBuilder;
    }

    private static async Task<IResult> GetSharedBills(
        string? token,
        IPersonAccessLinksService personAccessLinksService,
        CancellationToken ct)
    {
        var result = await personAccessLinksService.ValidateTokenAsync(token, ct);

        // Missing/blank token is a client error (400 ValidationProblem, errors.token); any other
        // failure just means the token is not valid → 200 false.
        if (result.Error is ValidationError)
            return result.ToHttpResult();

        return Results.Ok(result.IsSuccess);
    }
}
