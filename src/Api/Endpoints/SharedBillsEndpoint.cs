using Application.Abstractions.Services;

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
        HttpRequest req,
        IPersonAccessLinksService personAccessLinksService,
        CancellationToken ct)
    {
        var token = req.Query["token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest("The token cannot be null nor empty");

        var result = await personAccessLinksService.ValidateTokenAsync(token, ct);

        return Results.Ok(result.IsSuccess);
    }
}
