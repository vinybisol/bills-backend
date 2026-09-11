using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IPersonAccessLinksService
{
    Task<Result> ValidateTokenAsync(string token, CancellationToken cancellationToken);
    Task<Result<PersonAccessLinkDto>> CreateAsync(long personId, CancellationToken cancellationToken);
    Task<Result> RevokeAsync(long id, CancellationToken cancellationToken);
}