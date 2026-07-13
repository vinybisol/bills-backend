using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;

namespace Application.Services;

internal sealed class CategoryService(
    ICategoryRepository repository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : ICategoryService
{
    public async Task<Result<CategoryDto>> CreateCategoryAsync(string name, CancellationToken cancellationToken)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            return Error.Validation("Category name cannot be empty ou null");

        if (await repository.ExistsByNameAsync(trimmedName, cancellationToken))
            return Error.Conflict("A category with that name already exists.");

        var category = Category.Create(currentOwner.Id, trimmedName, timeProvider.GetUtcNow());
        repository.Add(category);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CategoryDto(category.Id, category.Name));
    }

    public async Task AddRangeAsync(IEnumerable<Category> categories, CancellationToken ct)
    {
        repository.AddRange(categories);

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<Result<CategoryDto>> UpdateAsync(long id, string name, CancellationToken ct)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            return Error.Validation("Category name cannot be empty ou null");

        var category = await repository.GetByIdAsync(id, ct);
        if (category is null)
            return Error.NotFound(nameof(category));

        if (await repository.ExistsByNameAsync(trimmedName, ct))
            return Error.Conflict("A category with that name already exists.");

        category.Rename(trimmedName);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new CategoryDto(category.Id, category.Name));
    }

    public async Task<Result<IEnumerable<CategoryDto>>> GetAllByNameAsync(CancellationToken ct)
    {
        var pagedQuery = new PagedQueryDto<Category>(1000, 0, c => c.Name);

        var result = await repository.GetAllByNameAsync(pagedQuery, ct);

        return Result.Success(result);
    }

    public async Task<Result> DeleteByIdAsync(long id, CancellationToken ct)
    {
        var category = await repository.GetByIdAsync(id, ct);
        if (category is null)
            return Error.NotFound("Categoria");

        category.Deactivate();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}