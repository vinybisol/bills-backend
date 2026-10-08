using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs;
using Application.DTOs.Services.PesonAccess;
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

        if (expiresAt.HasValue && expiresAt.Value <= timeProvider.GetUtcNow())
            return Error.Validation("Token with worng expire time");

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

    public async Task<Result<IReadOnlyCollection<PesonAccessLinkDto>>> GetAllAsync(CancellationToken ct)
    {
        var pagedQuery = new PagedQueryDto<PersonAccessLink, DateTimeOffset>(1000, 0, c => c.CreatedAt);
        var result = await repository.GetAllAsync(pagedQuery, ct);
        return Result.Success(result);
    }

    public async Task<Result> RevokeAsync(long id, CancellationToken ct)
    {
        var personAccessLink = await repository.GetByIdAsync(id, ct);
        if (personAccessLink is null)
            return Error.NotFound(nameof(PersonAccessLink));

        if (personAccessLink.RevokeAt.HasValue)
            return Result.Success();

        personAccessLink.Revoke(timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private const string TokenField = "token";

    public async Task<Result> ValidateTokenAsync(string token, CancellationToken ct)
    {
        var tokenResult = sharedPagesTokenService.Validate(token);
        if (tokenResult.IsFailure)
            return tokenResult;

        var pesonAccessLinkTokenDto = tokenResult.Value;
        var personAccessLink = await repository.GetByTokenIdAsync(pesonAccessLinkTokenDto.TokenId, ct);
        if (personAccessLink is null)
            return Result.Failure(Error.Forbidden());

        if (personAccessLink.RevokeAt <= timeProvider.GetUtcNow())
            return Result.Failure(Error.Forbidden());

        return Result.Success();
    }
}