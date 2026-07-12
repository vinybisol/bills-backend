using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Domain.Entities;

namespace Application.Abstractions.Repositories;

/// <summary>
/// Persists <see cref="Category"/> instances, isolating <see cref="Application"/> services from
/// the concrete persistence technology.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Persists a batch of newly created categories.
    /// </summary>
    /// <param name="categories">The categories to add.</param>
    void Add(Category categories);
    void AddRange(IEnumerable<Category> categories);
    Task<Category?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IEnumerable<CategoryDto>> GetAllByNameAsync(IPagedQuery<Category> pagedQuery, CancellationToken cancellationToken);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);
}
