using Domain.Entities;

namespace Application.Abstractions.Services;

public interface ICategoryService
{
    Task AddRangeAsync(IEnumerable<Category> categories, CancellationToken cancellationToken);
}
