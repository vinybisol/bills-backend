using Application.Abstractions.Repositories;
using Data.Contexts;
using Domain.Entities;

namespace Data.Repositories;

/// <summary>
/// Default <see cref="ICategoryRepository"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to persist categories.</param>
internal sealed class CategoryRepository(AppDbContext db) : ICategoryRepository
{
    /// <inheritdoc/>
    public void AddRange(IEnumerable<Category> categories)
    {
        db.Categories.AddRange(categories);
    }
}
