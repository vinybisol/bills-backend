using Application.Abstractions.Repositories;
using Application.DTOs.Services.PesonAccess;
using Application.Abstractions.Repositories.Strategies;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class PersonAccessLinksRepository(AppDbContext db) : IPersonAccessLinksRepository
{
    private readonly DbSet<PersonAccessLink> _entity = db.PersonAccessLink;

    public void Add(PersonAccessLink personAccessLink)
        => _entity.Add(personAccessLink);

    public async Task<bool> ExistsByPersonIdAsync(long personId, CancellationToken ct)
        => await _entity.AnyAsync(a => a.PersonId == personId, ct);

    public async Task<PersonAccessLink?> GetByIdAsync(long id, CancellationToken ct)
        => await _entity.FirstOrDefaultAsync(f => f.Id == id, ct);
    public async Task<PersonAccessLink?> GetByPersonIdAsync(long personId, CancellationToken ct)
        => await _entity.FirstOrDefaultAsync(f => f.PersonId == personId, ct);

    public async Task<PersonAccessLink?> GetByTokenIdAsync(Guid tokenId, CancellationToken ct)
        => await _entity.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.TokenId == tokenId, ct);

    public async Task<IReadOnlyCollection<PesonAccessLinkDto>> GetAllAsync(IPagedQuery<PersonAccessLink, DateTimeOffset> pagedQuery, CancellationToken ct)
        => await _entity
            .AsNoTracking()
            .OrderBy(pagedQuery.OrderBy)
            .Skip(pagedQuery.Skip)
            .Take(pagedQuery.Take)
            .Select(s => new PesonAccessLinkDto(s.Id, s.PersonId, s.ExpiresAt, s.RevokeAt))
            .ToListAsync(ct);

}