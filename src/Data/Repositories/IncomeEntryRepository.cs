using Application.Abstractions.Repositories;
using Application.DTOs.Services;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class IncomeEntryRepository(AppDbContext db) : IIncomeEntryRepository
{
    private readonly DbSet<IncomeEntry> _entity = db.IncomeEntries;

    public void Add(IncomeEntry entry) => _entity.Add(entry);

    public void AddRange(IEnumerable<IncomeEntry> entries) => _entity.AddRange(entries);

    public void Remove(IncomeEntry entry) => _entity.Remove(entry);

    public async Task<IncomeEntry?> GetByIdAsync(long id, CancellationToken ct) => await _entity.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlySet<(long TemplateId, int Month)>> GetExistingMonthsAsync(int year, CancellationToken ct)
    {
        var existing = await _entity
            .AsNoTracking()
            .Where(e => e.RefYear == year)
            .Select(e => new { e.IncomeId, e.RefMonth })
            .ToListAsync(ct);

        return existing.Select(e => (e.IncomeId, e.RefMonth)).ToHashSet();
    }

    // IgnoreQueryFilters + explicit owner checks: the template may have been deactivated since. IgnoreQueryFilters
    // in a subquery disables the global filters for the WHOLE query, so the root must also filter by owner.
    public async Task<IReadOnlyList<IncomeEntryWithNameDto>> GetMonthWithNameAsync(int year, int month, long ownerId, CancellationToken ct)
    {
        var rows = await _entity
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.RefYear == year && e.RefMonth == month)
            .Select(e => new
            {
                Entry = e,
                Name = db.Incomes.IgnoreQueryFilters()
                    .Where(i => i.Id == e.IncomeId && i.OwnerId == ownerId)
                    .Select(i => i.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows.Select(r => new IncomeEntryWithNameDto(r.Entry, r.Name ?? string.Empty)).ToList();
    }
}
