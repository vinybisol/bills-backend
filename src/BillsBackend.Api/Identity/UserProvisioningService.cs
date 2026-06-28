using BillsBackend.Api.Data;
using BillsBackend.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Identity;

/// <summary>
/// Default <see cref="IUserProvisioningService"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to look up and persist users.</param>
/// <param name="timeProvider">The clock used to stamp newly provisioned users.</param>
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = AppUser.Provision(firebaseUid, email, timeProvider.GetUtcNow());

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
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
}
