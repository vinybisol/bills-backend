using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Default <see cref="IUserProvisioningService"/>, backed by <see cref="IAppUserService"/>
/// and <see cref="ICategoryRepository"/>.
/// </summary>
/// <param name="usersService">The repository used to look up and persist users.</param>
/// <param name="categoriesService">The repository used to seed default categories for new users.</param>
/// <param name="timeProvider">The clock used to stamp newly provisioned users and seed categories.</param>
/// <param name="logger">The logger used to record provisioning events.</param>
public sealed class UserProvisioningService(
    IAppUserService usersService,
    ICategoryService categoriesService,
    TimeProvider timeProvider,
    ILogger<UserProvisioningService> logger) : IUserProvisioningService
{
    /// <inheritdoc/>
    public async Task<AppUser> GetOrCreateAsync(
        string firebaseUid,
        string? email,
        string? name,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);

        var existing = await usersService.FindByFirebaseUidAsync(firebaseUid, ct);
        if (existing is not null)
        {
            return existing;
        }

        var user = AppUser.Provision(firebaseUid, email, name, timeProvider.GetUtcNow());

        var (persisted, wasCreated) = await usersService.AddAsync(user, ct);

        if (wasCreated)
        {
            var now = timeProvider.GetUtcNow();
            await categoriesService.AddRangeAsync(
                Category.DefaultNames.Select(n => Category.Create(persisted.Id, n, now)),
                ct);
            logger.LogInformation("Provisioned new app_user {UserId} on first authenticated request.", persisted.Id);
        }

        return persisted;
    }
}
