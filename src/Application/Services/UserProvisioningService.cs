using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Default <see cref="IUserProvisioningService"/>, backed by <see cref="IAppUserRepository"/>
/// and <see cref="ICategoryRepository"/>.
/// </summary>
/// <param name="users">The repository used to look up and persist users.</param>
/// <param name="categories">The repository used to seed default categories for new users.</param>
/// <param name="timeProvider">The clock used to stamp newly provisioned users and seed categories.</param>
/// <param name="logger">The logger used to record provisioning events.</param>
public sealed class UserProvisioningService(
    IAppUserRepository users,
    ICategoryRepository categories,
    TimeProvider timeProvider,
    ILogger<UserProvisioningService> logger) : IUserProvisioningService
{
    /// <inheritdoc/>
    public async Task<AppUser> GetOrCreateAsync(
        string firebaseUid,
        string? email,
        string? name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);

        var existing = await users.FindByFirebaseUidAsync(firebaseUid, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = AppUser.Provision(firebaseUid, email, name, timeProvider.GetUtcNow());

        var (persisted, wasCreated) = await users.AddAsync(user, cancellationToken);

        if (wasCreated)
        {
            var now = timeProvider.GetUtcNow();
            await categories.AddRangeAsync(
                Category.DefaultNames.Select(n => Category.Create(persisted.Id, n, now)),
                cancellationToken);
            logger.LogInformation("Provisioned new app_user {UserId} on first authenticated request.", persisted.Id);
        }

        return persisted;
    }
}
