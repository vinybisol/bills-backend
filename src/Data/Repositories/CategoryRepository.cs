using Application.Abstractions.Repositories;
using Data.Contexts;
using Domain.Entities;

namespace Data.Repositories;

/// <summary>
/// Default <see cref="ICategoryRepository"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to persist categories.</param>
public sealed class CategoryRepository(AppDbContext db) : ICategoryRepository
{
    /// <inheritdoc/>
    public async Task AddRangeAsync(IEnumerable<Category> categories, CancellationToken cancellationToken = default)
    {
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync(cancellationToken);
    }
}
