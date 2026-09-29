using Application.Abstractions.Repositories;
using Application.DTOs.Services;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class BillEntryRepository(AppDbContext db) : IBillEntryRepository
{
    private readonly DbSet<BillEntry> _entity = db.BillEntries;

    public void Add(BillEntry entry) => _entity.Add(entry);

    public void AddRange(IEnumerable<BillEntry> entries) => _entity.AddRange(entries);

    public void Remove(BillEntry entry) => _entity.Remove(entry);

    public async Task<BillEntry?> GetByIdAsync(long id, CancellationToken ct) => await _entity.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlySet<(long TemplateId, int Month)>> GetExistingMonthsAsync(int year, CancellationToken ct)
    {
        var existing = await _entity
            .AsNoTracking()
            .Where(e => e.RefYear == year)
            .Select(e => new { e.BillId, e.RefMonth })
            .ToListAsync(ct);

        return existing.Select(e => (e.BillId, e.RefMonth)).ToHashSet();
    }

    public async Task<IReadOnlyList<BillEntry>> GetByBillFromMonthAsync(long billId, int fromYear, int fromMonth, CancellationToken ct) => await _entity
        .Where(e => e.BillId == billId &&
                    (e.RefYear > fromYear || (e.RefYear == fromYear && e.RefMonth >= fromMonth)))
        .ToListAsync(ct);

    public async Task<IReadOnlyList<BillEntry>> GetByBillAsync(long billId, CancellationToken ct) => await _entity
        .AsNoTracking()
        .Where(e => e.BillId == billId)
        .ToListAsync(ct);

    // IgnoreQueryFilters + explicit owner checks: entries snapshot a template/category/person that
    // may have been deactivated since, and the listing must still show their names. IgnoreQueryFilters
    // in a subquery disables the global filters for the WHOLE query, so the root must also filter by owner.
    public async Task<IReadOnlyList<BillEntryWithNamesDto>> GetMonthWithNamesAsync(int year, int month, long ownerId, CancellationToken ct)
    {
        var rows = await _entity
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.RefYear == year && e.RefMonth == month)
            .Select(e => new
            {
                Entry = e,
                Bill = db.Bills.IgnoreQueryFilters()
                    .Where(b => b.Id == e.BillId && b.OwnerId == ownerId)
                    .Select(b => new
                    {
                        b.Name,
                        Category = db.Categories.IgnoreQueryFilters()
                            .Where(c => c.Id == b.CategoryId && c.OwnerId == ownerId)
                            .Select(c => c.Name)
                            .FirstOrDefault()
                    })
                    .FirstOrDefault(),
                Person = db.Persons.IgnoreQueryFilters()
                    .Where(p => p.Id == e.PersonId && p.OwnerId == ownerId)
                    .Select(p => p.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new BillEntryWithNamesDto(r.Entry, r.Bill?.Name ?? string.Empty, r.Bill?.Category ?? string.Empty, r.Person))
            .ToList();
    }
}
