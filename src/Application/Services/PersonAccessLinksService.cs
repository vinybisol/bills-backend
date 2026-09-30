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
    ISharedPagesTokenService sharedPagesTokenService,
    TimeProvider timeProvider) : IPersonAccessLinksService
{
    public async Task<Result<string>> CreateAsync(long personId, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        if (personId <= 0)
            return Error.Validation("Person id cannot be less or equals zero");

        var person = await personRepository.GetByIdAsync(personId, ct);
        if (person is null)
            return Error.NotFound(nameof(Person));

        var personAccessLinkExists = await repository.ExistsByPersonIdAsync(personId, ct);
        if (personAccessLinkExists)
            return Error.Conflict(nameof(PersonAccessLink));


        var sharedPagesTokenRes = sharedPagesTokenService.Issue(personId, expiresAt);

        if (sharedPagesTokenRes.IsFailure)
            return Error.InvalidOperation;

        var sharedPagesToken = sharedPagesTokenRes.Value;
        var personAccessLink = PersonAccessLink.Create(
            currentOwner.Id, person.Id,
            sharedPagesToken.TokenId,
            sharedPagesToken.ExpiresAt,
            timeProvider.GetUtcNow());

        repository.Add(personAccessLink);

        await unitOfWork.SaveChangesAsync(ct);
        return sharedPagesToken.Token;
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

    public async Task<Result> ValidateTokenAsync(string token, CancellationToken ct)
    {
        var tokenResult = sharedPagesTokenService.Validate(token);
        if (tokenResult.IsFailure)
            return tokenResult;

        var pesonAccessLinkTokenDto = tokenResult.Value;
        var personAccessLink = await repository.GetByTokenIdAsync(pesonAccessLinkTokenDto.TokenId, ct);
        if (personAccessLink is null)
            return Result.Failure(Error.Forbidden());

        if (personAccessLink.Active is false)
            return Result.Failure(Error.Forbidden());

        return Result.Success();
    }
}