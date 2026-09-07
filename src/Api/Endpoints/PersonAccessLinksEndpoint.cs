using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class PersonAccessLinksEndpoint
{
    public static RouteGroupBuilder MapAccessLinkEndpoints(this RouteGroupBuilder group)
    {
        var accessLinkGroup = group
            .MapGroup("persons/access-links")
        .AddEndpointFilter<UserEndpointFilter>();

        accessLinkGroup.MapPost("", CreateAccessLink);
        accessLinkGroup.MapPut("/{id:long}/revoke", RevokeAccessLink);

        return group;
    }

    private static async Task<IResult> CreateAccessLink(
        CreateAccessLinkRequest createAccessLinkRequest,
        IPersonAccessLinksService service,
        CancellationToken ct)
    {
        var result = await service.CreateAsync(createAccessLinkRequest.Id, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeAccessLink(
        long id,
        IPersonAccessLinksService service,
        CancellationToken ct)
    {
        var result = await service.RevokeAsync(id, ct);
        return result.ToHttpResult();
    }
}
