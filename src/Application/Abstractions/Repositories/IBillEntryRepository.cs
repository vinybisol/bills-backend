using Application.DTOs.Services;
using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IBillEntryRepository
{
    void Add(BillEntry entry);
    void AddRange(IEnumerable<BillEntry> entries);
    void Remove(BillEntry entry);

    /// <summary>Returns the owner's tracked entry, or <see langword="null"/> when it does not exist or belongs to another owner.</summary>
    Task<BillEntry?> GetByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>Returns the (bill id, month) pairs that already have an entry in the given year.</summary>
    Task<IReadOnlySet<(long TemplateId, int Month)>> GetExistingMonthsAsync(int year, CancellationToken cancellationToken);

    /// <summary>Returns the tracked entries of a bill from the given month (inclusive) onward.</summary>
    Task<IReadOnlyList<BillEntry>> GetByBillFromMonthAsync(long billId, int fromYear, int fromMonth, CancellationToken cancellationToken);

    /// <summary>Returns all entries of a bill, read-only.</summary>
    Task<IReadOnlyList<BillEntry>> GetByBillAsync(long billId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the owner's entries of a month, read-only, with their bill, category and person names.
    /// Names resolve even when the template, category or person have since been deactivated.
    /// </summary>
    Task<IReadOnlyList<BillEntryWithNamesDto>> GetMonthWithNamesAsync(int year, int month, long ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the owner's entries by person of a month, read-only, with their bill, category and person names.
    /// Names resolve even when the template, category or person have since been deactivated.
    /// </summary>
    Task<IReadOnlyList<BillEntryWithNamesDto>> GetMonthByPersonIdWithNamesAsync(int year, int month, long ownerId, long personId, CancellationToken ct);

    /// <summary>
    /// Returns the owner's entries of a whole year, read-only, with their bill, category and person names.
    /// Names resolve even when the template, category or person have since been deactivated.
    /// </summary>
    Task<IReadOnlyList<BillEntryWithNamesDto>> GetYearWithNamesAsync(int year, long ownerId, CancellationToken cancellationToken);

    /// <summary>Returns the owner's tracked entries among <paramref name="ids"/>; unknown or foreign ids are simply absent.</summary>
    Task<IReadOnlyList<BillEntry>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the owner's receivable entries (split ratio &lt; 1) of a person, read-only, with their
    /// bill, category and person names (resolved even when since deactivated).
    /// </summary>
    Task<IReadOnlyList<BillEntryWithNamesDto>> GetReceivablesByPersonWithNamesAsync(long personId, long ownerId, CancellationToken cancellationToken);
}
