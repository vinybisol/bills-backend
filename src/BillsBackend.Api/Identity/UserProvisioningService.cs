using BillsBackend.Api.Data;
using BillsBackend.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Identity;

/// <summary>
/// Default <see cref="IUserProvisioningService"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to look up and persist users.</param>
/// <param name="timeProvider">The clock used to stamp newly provisioned users and seed categories.</param>
/// <param name="logger">The logger used to record provisioning events.</param>
public sealed class UserProvisioningService(
    AppDbContext db,
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

        var existing = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = AppUser.Provision(firebaseUid, email, name, timeProvider.GetUtcNow());

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await SeedDefaultCategoriesAsync(user.Id, cancellationToken);
            logger.LogInformation("Provisioned new app_user {UserId} on first authenticated request.", user.Id);
            return user;
        }
        catch (DbUpdateException)
        {
            // A concurrent request provisioned the same Firebase uid first and won the
            // race on the unique index; fall back to the row that was committed.
            db.Entry(user).State = EntityState.Detached;
            var concurrent = await db.Users
                .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task SeedDefaultCategoriesAsync(long ownerId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var name in Category.DefaultNames)
        {
            db.Categories.Add(Category.Create(ownerId, name, now));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
