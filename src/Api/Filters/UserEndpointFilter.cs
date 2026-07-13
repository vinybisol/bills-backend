using Api.Identity;
using Application.Abstractions.Services;
using Domain.Abstractions.Filters;

namespace Api.Filters;


public sealed class UserEndpointFilter(
                IUserProvisioningService provisioning,
                   ICurrentOwner currentOwner) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user is null)
            return Results.Unauthorized();

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), CancellationToken.None);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var result = await next(context);
        return result;
    }
}