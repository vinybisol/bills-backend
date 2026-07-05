using Application.Abstractions.Repositories;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

/// <summary>
/// Default <see cref="IAppUserRepository"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to look up and persist users.</param>
public sealed class AppUserRepository(AppDbContext db) : IAppUserRepository
{
    /// <inheritdoc/>
    public async Task<AppUser?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken cancellationToken = default) =>
        await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, cancellationToken);

    /// <inheritdoc/>
    public async Task<UserProvisioningResult> AddAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new UserProvisioningResult(user, true);
        }
        catch (DbUpdateException)
        {
            // A concurrent request provisioned the same Firebase uid first and won the
            // race on the unique index; fall back to the row that was committed.
            db.Entry(user).State = EntityState.Detached;
            var concurrent = await db.Users
                .FirstOrDefaultAsync(u => u.FirebaseUid == user.FirebaseUid, cancellationToken);
            if (concurrent is not null)
            {
                return new UserProvisioningResult(concurrent, false);
            }

            throw;
        }
    }
}
