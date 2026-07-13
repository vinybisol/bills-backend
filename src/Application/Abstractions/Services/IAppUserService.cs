using Domain.Entities;

namespace Application.Abstractions.Services;

public interface IAppUserService
{
    /// <summary>
    /// Looks up an <see cref="AppUser"/> by its external Firebase identifier.
    /// </summary>
    /// <param name="firebaseUid">The Firebase user identifier to search for.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>The matching <see cref="AppUser"/>, or <see langword="null"/> when none exists.</returns>
    Task<AppUser?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a newly provisioned <see cref="AppUser"/>, tolerating a concurrent provisioning
    /// race on the same Firebase uid.
    /// </summary>
    /// <param name="user">The user to persist.</param>
    /// <param name="ct">The token to observe for cancellation.</param>
    /// <returns>
    /// The persisted user together with whether this call actually created it, or lost the race
    /// to a concurrent request and returned the row that request committed instead.
    /// </returns>
    Task<UserProvisioningResult> AddAsync(AppUser user, CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of <see cref="IAppUserService.AddAsync"/>.
/// </summary>
/// <param name="User">The persisted user — either the one just created, or the concurrent winner.</param>
/// <param name="WasCreated">
/// <see langword="true"/> when this call created <paramref name="User"/>;
/// <see langword="false"/> when a concurrent request won the race and <paramref name="User"/>
/// is that request's row.
/// </param>
public readonly record struct UserProvisioningResult(AppUser User, bool WasCreated);