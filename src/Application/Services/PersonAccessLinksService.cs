using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;

namespace Application.Services;

internal sealed class PersonAccessLinksService(
    IPersonRepository personRepository,
    IPersonAccessLinksRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider) : IPersonAccessLinksService
{
    public async Task<Result<string>> CreateAsync(long personId, CancellationToken ct)
    {
        if (personId <= 0)
            return Error.Validation("Person id cannot be less or equals zero");

        var person = await personRepository.GetByIdAsync(personId, ct);
        if (person is null)
            return Error.NotFound(nameof(Person));

        var personAccessLinkExists = await repository.ExistsByPersonIdAsync(personId, ct);
        if (personAccessLinkExists)
            return Error.Conflict(nameof(PersonAccessLink));

        var tokenPlano = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace("+", "").Replace("/", "").Replace("=", "");

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(tokenPlano))
        );

        var personAccessLink = PersonAccessLink.Create(currentOwner.Id, person.Id, tokenHash, timeProvider.GetUtcNow());
        repository.Add(personAccessLink);

        await unitOfWork.SaveChangesAsync(ct);

        return tokenPlano;
    }

    public async Task<Result> RevokeAsync(long id, CancellationToken ct)
    {
        var personAccessLink = await repository.GetByIdAsync(id, ct);
        if (personAccessLink is null)
            return Error.NotFound(nameof(PersonAccessLink));

        personAccessLink.Revoke(timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}