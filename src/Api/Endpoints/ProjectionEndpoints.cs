using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class ProjectionEndpoints
{
    public static RouteGroupBuilder MapProjectionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/projection/{year:int}", CreateProjection)
            .AddEndpointFilter<UserEndpointFilter>();
        return group;
    }

    private static async Task<IResult> CreateProjection(
        int year,
        IProjectionService projectionService,
        CancellationToken ct)
    {
        var result = await projectionService.ProjectYearAsync(year, ct);

        return result.ToHttpResult();
    }
}
