using BillsBackend.Api.Domain;

namespace BillsBackend.Api.Identity;

/// <summary>
/// Translates an external Firebase identity into the internal <see cref="AppUser"/>,
/// provisioning the user just-in-time on first sight.
/// </summary>
public interface IUserProvisioningService
{
    /// <summary>
    /// Resolves the internal user for the given Firebase uid, creating it when it does not yet exist.
    /// </summary>
    /// <param name="firebaseUid">The Firebase user identifier taken from the validated token.</param>
    /// <param name="email">The e-mail address from the token, or <see langword="null"/> when absent.</param>
    /// <param name="cancellationToken">The token to observe for cancellation.</param>
    /// <returns>The existing or newly created <see cref="AppUser"/> mapped to <paramref name="firebaseUid"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="firebaseUid"/> is <see langword="null"/> or empty.</exception>
    Task<AppUser> GetOrCreateAsync(string firebaseUid, string? email, CancellationToken cancellationToken = default);
}
