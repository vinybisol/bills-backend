using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services.PesonAccess;
using Domain.Abstractions;
using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IPersonAccessLinksRepository
{
    void Add(PersonAccessLink personAccessLink);
    Task<PersonAccessLink?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<PersonAccessLink?> GetByPersonIdAsync(long personId, CancellationToken cancellationToken);
    Task<bool> ExistsByPersonIdAsync(long personId, CancellationToken cancellationToken);
    Task<PersonAccessLink?> GetByTokenIdAsync(Guid tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PesonAccessLinkDto>> GetAllAsync(IPagedQuery<PersonAccessLink, DateTimeOffset> pagedQuery, CancellationToken ct);
}