using BillsBackend.Api.Contracts;
using BillsBackend.Api.Identity;

namespace BillsBackend.Api.Endpoints;

internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/health", GetHealth);
        group.MapGet("/me", GetMe);
        return group;
    }

    // Authenticated liveness endpoint: resolves (and just-in-time provisions) the internal
    // app_user from the Firebase token and returns its internal id.
    private static async Task<IResult> GetHealth(
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        CancellationToken cancellationToken)
    {
        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
        {
            return Results.Unauthorized();
        }

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), cancellationToken);
        return Results.Ok(new HealthResponse(appUser.Id, "healthy"));
    }

    // Returns the logged-in user's internal profile, resolving (and just-in-time provisioning)
    // the app_user from the Firebase token.
    private static async Task<IResult> GetMe(
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        CancellationToken cancellationToken)
    {
        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
        {
            return Results.Unauthorized();
        }

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), cancellationToken);
        return Results.Ok(new MeResponse(appUser.Id, appUser.Name, appUser.Email));
    }
}
