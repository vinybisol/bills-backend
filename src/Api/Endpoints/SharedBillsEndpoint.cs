namespace Api.Endpoints;

internal static class SharedBillsEndpoint
{
    public static RouteGroupBuilder MapSharedBillsEndpoint(this RouteGroupBuilder groupBuilder)
    {
        var group = groupBuilder
        .MapGroup("/shared")
        .AllowAnonymous();

        group.MapGet("", GetSharedBills);

        return groupBuilder;
    }

    private static async Task<IResult> GetSharedBills(HttpRequest req)
    {
        var token = req.Query["token"];
        await Task.FromResult("ola");
        return Results.Ok(token);
    }
}
