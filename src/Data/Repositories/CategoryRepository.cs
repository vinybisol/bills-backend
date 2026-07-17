using Application.Abstractions.Repositories;
using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

/// <summary>
/// Default <see cref="ICategoryRepository"/> backed by the <see cref="AppDbContext"/>.
/// </summary>
/// <param name="db">The database context used to persist categories.</param>
internal sealed class CategoryRepository(AppDbContext db) : ICategoryRepository
{
    private readonly DbSet<Category> _entity = db.Categories;

    /// <inheritdoc/>
    public void Add(Category category) => _entity.Add(category);

    /// <inheritdoc/>
    public void AddRange(IEnumerable<Category> categories) => _entity.AddRange(categories);

    /// <inheritdoc/>
    public async Task<Category?> GetByIdAsync(long id, CancellationToken ct) => await _entity.FirstOrDefaultAsync(f => f.Id == id, ct);

    /// <inheritdoc/>
    public async Task<IEnumerable<CategoryDto>> GetAllByNameAsync(IPagedQuery<Category> pagedQuery, CancellationToken ct) => await _entity
        .AsNoTracking()
        .OrderBy(pagedQuery.OrderBy)
        .Skip(pagedQuery.Skip)
        .Take(pagedQuery.Take)
        .Select(s => new CategoryDto(s.Id, s.Name))
        .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct) => await _entity.AnyAsync(f => f.Name == name, ct);
}
