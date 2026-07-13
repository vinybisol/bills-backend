using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Entities;

namespace Application.Abstractions.Services;

public interface ICategoryService
{
    Task<Result<CategoryDto>> CreateCategoryAsync(string name, CancellationToken cancellationToken);
    Task AddRangeAsync(IEnumerable<Category> categories, CancellationToken cancellationToken);
    Task<Result<CategoryDto>> UpdateAsync(long id, string name, CancellationToken cancellationToken);
    Task<Result<IEnumerable<CategoryDto>>> GetAllByNameAsync(CancellationToken cancellationToken);
    Task<Result> DeleteByIdAsync(long id, CancellationToken cancellationToken);
}
