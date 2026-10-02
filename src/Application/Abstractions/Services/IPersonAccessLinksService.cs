using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IPersonAccessLinksService
{
    /// <summary>
    /// Checks a shared access token. A blank token is a <see cref="ValidationError"/> (field <c>token</c>);
    /// any other failure (malformed/unknown token) means "not valid".
    /// </summary>
    Task<Result> ValidateTokenAsync(string? token, CancellationToken cancellationToken);
    Task<Result<PersonAccessLinkDto>> CreateAsync(long personId, CancellationToken cancellationToken);
    Task<Result> RevokeAsync(long id, CancellationToken cancellationToken);
}