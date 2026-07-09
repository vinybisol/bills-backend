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
    void AddRange(IEnumerable<Category> categories);
}
