using Application.DTOs.Services.PesonAccess;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IPersonAccessLinksService
{
    Task<Result<ValidatePesonAccessLinkDto>> ValidateTokenAsync(string? token, CancellationToken cancellationToken);
    Task<Result<CreatePesonAccessLinkDto>> CreateAsync(long personId, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task<Result> RevokeAsync(long id, CancellationToken cancellationToken);
    Task<Result<IEnumerable<PesonAccessLinkDto>>> GetAllAsync(CancellationToken cancellationToken);
}