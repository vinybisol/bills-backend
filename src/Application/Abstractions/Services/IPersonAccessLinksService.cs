using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IPersonAccessLinksService
{
    Task<Result<string>> CreateAsync(long personId, CancellationToken cancellationToken);
    Task<Result> RevokeAsync(long id, CancellationToken cancellationToken);
}