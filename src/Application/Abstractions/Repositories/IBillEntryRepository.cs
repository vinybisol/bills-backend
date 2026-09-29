using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IBillEntryRepository
{
    /// <summary>Returns the tracked entries of a bill from the given month (inclusive) onward.</summary>
    Task<IReadOnlyList<BillEntry>> GetByBillFromMonthAsync(long billId, int fromYear, int fromMonth, CancellationToken cancellationToken);

    /// <summary>Returns all entries of a bill, read-only.</summary>
    Task<IReadOnlyList<BillEntry>> GetByBillAsync(long billId, CancellationToken cancellationToken);
}
