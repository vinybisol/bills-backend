using Application.Abstractions.Repositories;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

/// <summary>
/// Default <see cref="IAppUserRepository"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to look up and persist users.</param>
internal sealed class AppUserRepository(AppDbContext db) : IAppUserRepository
{
    /// <inheritdoc/>
    public void Add(AppUser user)
        => db.Users.Add(user);

    /// <inheritdoc/>
    public async Task<AppUser?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken ct) =>
        await db.Users
            .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, ct);
}
