using Application.Abstractions.Repositories;
using Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

/// <summary>
/// Default <see cref="IHealthRepository"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to probe connectivity and migrations.</param>
internal sealed class HealthRepository(AppDbContext db) : IHealthRepository
{
    /// <inheritdoc/>
    public Task<bool> CanConnectAsync(CancellationToken ct) => db.Database.CanConnectAsync(ct);

    /// <inheritdoc/>
    public async Task<int> CountPendingMigrationsAsync(CancellationToken ct) =>
        (await db.Database.GetPendingMigrationsAsync(ct)).Count();
}
