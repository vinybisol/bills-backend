using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
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
    public async Task<Result<PersonAccessLinkDto>> CreateAsync(long personId, CancellationToken ct)
    {
        if (personId <= 0)
            return Error.Validation("Person id cannot be less or equals zero");

        var person = await personRepository.GetByIdAsync(personId, ct);
        if (person is null)
            return Error.NotFound(nameof(Person));

        var personAccessLinkExists = await repository.ExistsByPersonIdAsync(personId, ct);
        if (personAccessLinkExists)
            return Error.Conflict(nameof(PersonAccessLink));

        var plainToken = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(plainToken)));

        var accessToken = new PesonAccessLinkTokenDto(personId, tokenHash);

        var encodedToken = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(accessToken));

        var personAccessLink = PersonAccessLink.Create(currentOwner.Id, person.Id, tokenHash, timeProvider.GetUtcNow());
        repository.Add(personAccessLink);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Create(new PersonAccessLinkDto(personAccessLink.Id, encodedToken));
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

    private const string TokenField = "token";

    public async Task<Result> ValidateTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new ValidationError([new Error(TokenField, "The token cannot be null nor empty.", ErrorType.Validation)]);

        try
        {
            var json = Base64Url.DecodeFromChars(token);
            var pesonAccessLinkTokenDto = JsonSerializer.Deserialize<PesonAccessLinkTokenDto>(json);

            if (pesonAccessLinkTokenDto is null)
                return Result.Failure(Error.InvalidOperation);

            if (string.IsNullOrWhiteSpace(pesonAccessLinkTokenDto.Token))
                return Result.Failure(Error.InvalidOperation);

            var personAccessLink = await repository.GetByPersonIdAndHashAsync(pesonAccessLinkTokenDto.PersonId, pesonAccessLinkTokenDto.Token, ct);
            if (personAccessLink is null)
                return Result.Failure(Error.NotFound(nameof(PersonAccessLink)));

            return Result.Success();
        }
        catch
        {
            return Result.Failure(Error.InvalidOperation);
        }
    }
}