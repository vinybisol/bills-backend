using Application.Abstractions.Repositories;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class IncomeEntryRepository(AppDbContext db) : IIncomeEntryRepository
{
    private readonly DbSet<IncomeEntry> _entity = db.IncomeEntries;

    public void AddRange(IEnumerable<IncomeEntry> entries) => _entity.AddRange(entries);

    public async Task<IReadOnlySet<(long TemplateId, int Month)>> GetExistingMonthsAsync(int year, CancellationToken ct)
    {
        var existing = await _entity
            .AsNoTracking()
            .Where(e => e.RefYear == year)
            .Select(e => new { e.IncomeId, e.RefMonth })
            .ToListAsync(ct);

        return existing.Select(e => (e.IncomeId, e.RefMonth)).ToHashSet();
    }
}
