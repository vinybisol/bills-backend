using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Domain.Abstractions;
using Application.DTOs.Services;
using Domain.Entities;
using Domain.Abstractions.Filters;
using Application.DTOs;

namespace Application.Services;

internal sealed class PersonService(
    IPersonRepository repository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IPersonService
{
    public async Task<Result<PersonDto>> CreateAsync(string name, CancellationToken ct)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            return Error.Validation("Person name cannot be empty ou null");

        if (await repository.ExistsByNameAsync(trimmedName, ct))
            return Error.Conflict("A person with that name already exists.");

        var person = Person.Create(currentOwner.Id, trimmedName, timeProvider.GetUtcNow());

        repository.Add(person);
        await unitOfWork.SaveChangesAsync(ct);

        return new PersonDto(person.Id, person.Name);
    }

    public async Task<Result<IEnumerable<PersonDto>>> GetAllByNameAsync(CancellationToken ct)
    {
        var pagedQuery = new PagedQueryDto<Person>(1000, 0, c => c.Name);

        var result = await repository.GetAllByNameAsync(pagedQuery, ct);
        if (result is null)
            return Result.Success(Enumerable.Empty<PersonDto>());

        return Result.Success(result);
    }

    public async Task<Result<PersonDto>> UpdateAsync(long id, string name, CancellationToken ct)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            return Error.Validation("Person name cannot be empty ou null");

        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
            return Error.NotFound(nameof(person));

        if (await repository.ExistsByNameAsync(trimmedName, ct))
            return Error.Conflict("A person with that name already exists.");

        person.Rename(trimmedName);

        await unitOfWork.SaveChangesAsync(ct);

        return new PersonDto(person.Id, person.Name);
    }

    public async Task<Result> DeleteByIdAsync(long id, CancellationToken ct)
    {
        var person = await repository.GetByIdAsync(id, ct);
        if (person is null)
            return Error.NotFound(nameof(person));

        person.Deactivate();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}