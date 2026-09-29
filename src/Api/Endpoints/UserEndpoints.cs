using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        // UserEndpointFilter provisions the app_user just-in-time and sets the current owner.
        var userGroup = group
            .MapGroup("")
            .AddEndpointFilter<UserEndpointFilter>();

        userGroup.MapGet("/health", GetHealth);
        userGroup.MapGet("/me", GetMe);

        return group;
    }

    private static async Task<IResult> GetHealth(
        IAppUserService appUserService,
        CancellationToken ct)
    {
        var result = await appUserService.GetCurrentAsync(ct);

        return result.IsSuccess
            ? Results.Ok(new HealthResponse(result.Value.Id, "healthy"))
            : result.ToHttpResult();
    }

    private static async Task<IResult> GetMe(
        IAppUserService appUserService,
        CancellationToken ct)
    {
        var result = await appUserService.GetCurrentAsync(ct);

        return result.ToHttpResult();
    }
}
