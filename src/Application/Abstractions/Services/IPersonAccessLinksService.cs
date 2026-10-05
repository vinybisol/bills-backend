using Application.DTOs.Services.PesonAccess;
using Domain.Abstractions;
using Domain.Entities;

namespace Application.Abstractions.Services;

public interface IPersonAccessLinksService
{
    Task<Result> ValidateTokenAsync(string token, CancellationToken cancellationToken);
    Task<Result<string>> CreateAsync(long personId, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task<Result> RevokeAsync(long id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyCollection<PesonAccessLinkDto>>> GetAllAsync(CancellationToken cancellationToken);
}