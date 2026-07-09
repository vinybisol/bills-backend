using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Domain.Entities;

namespace Application.Services;

internal sealed class CategoryService(
    ICategoryRepository repository,
    IUnitOfWork unitOfWork) : ICategoryService
{
    public async Task AddRangeAsync(IEnumerable<Category> categories, CancellationToken ct)
    {
        repository.AddRange(categories);

        await unitOfWork.SaveChangesAsync(ct);
    }
}