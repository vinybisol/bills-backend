using Application.Abstractions.Repositories;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class BillEntryRepository(AppDbContext db) : IBillEntryRepository
{
    private readonly DbSet<BillEntry> _entity = db.BillEntries;

    public async Task<IReadOnlyList<BillEntry>> GetByBillFromMonthAsync(long billId, int fromYear, int fromMonth, CancellationToken ct) => await _entity
        .Where(e => e.BillId == billId &&
                    (e.RefYear > fromYear || (e.RefYear == fromYear && e.RefMonth >= fromMonth)))
        .ToListAsync(ct);

    public async Task<IReadOnlyList<BillEntry>> GetByBillAsync(long billId, CancellationToken ct) => await _entity
        .AsNoTracking()
        .Where(e => e.BillId == billId)
        .ToListAsync(ct);
}
