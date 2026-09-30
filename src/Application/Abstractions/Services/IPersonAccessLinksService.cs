using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IPersonAccessLinksService
{
    Task<Result> ValidateTokenAsync(string token, CancellationToken cancellationToken);
    Task<Result<string>> CreateAsync(long personId, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task<Result> RevokeAsync(long id, CancellationToken cancellationToken);
}