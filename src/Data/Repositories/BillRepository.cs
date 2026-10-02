using Application.Abstractions.Repositories;
using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Data.Contexts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class BillRepository(AppDbContext db) : IBillRepository
{
    private readonly DbSet<Bill> _entity = db.Bills;

    public void Add(Bill bill) => _entity.Add(bill);

    public async Task<Bill?> GetByIdAsync(long id, CancellationToken ct) => await _entity.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IEnumerable<BillDto>> GetAllByNameAsync(IPagedQuery<Bill> pagedQuery, CancellationToken ct) => await _entity
        .AsNoTracking()
        .OrderBy(pagedQuery.OrderBy)
        .ThenBy(b => b.Id)
        .Skip(pagedQuery.Skip)
        .Take(pagedQuery.Take)
        .Select(b => new BillDto(b.Id, b.Name, b.CategoryId, b.Kind, b.DefaultAmount, b.SplitRatio, b.PersonId))
        .ToListAsync(ct);

    public async Task<IReadOnlyList<Bill>> GetRecurringAsync(CancellationToken ct) => await _entity
        .AsNoTracking()
        .Where(b => b.Kind == BillKindEnum.Recurring)
        .OrderBy(b => b.Id)
        .ToListAsync(ct);

    // IgnoreQueryFilters + explicit owner checks: a deactivated template (or its deactivated
    // category/person) must still resolve so its history stays readable.
    public async Task<BillHistoryHeaderDto?> GetHistoryHeaderAsync(long billId, long ownerId, CancellationToken ct) => await _entity
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(b => b.Id == billId && b.OwnerId == ownerId)
        .Select(b => new BillHistoryHeaderDto(
            b.Id,
            b.Name,
            db.Categories.IgnoreQueryFilters()
                .Where(c => c.Id == b.CategoryId && c.OwnerId == ownerId)
                .Select(c => c.Name)
                .FirstOrDefault() ?? string.Empty,
            b.SplitRatio,
            db.Persons.IgnoreQueryFilters()
                .Where(p => p.Id == b.PersonId && p.OwnerId == ownerId)
                .Select(p => p.Name)
                .FirstOrDefault()))
        .FirstOrDefaultAsync(ct);
}
