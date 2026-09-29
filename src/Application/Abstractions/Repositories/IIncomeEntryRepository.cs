using Application.DTOs.Services;
using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IIncomeEntryRepository
{
    void Add(IncomeEntry entry);
    void AddRange(IEnumerable<IncomeEntry> entries);
    void Remove(IncomeEntry entry);

    /// <summary>Returns the owner's tracked entry, or <see langword="null"/> when it does not exist or belongs to another owner.</summary>
    Task<IncomeEntry?> GetByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>Returns the (income id, month) pairs that already have an entry in the given year.</summary>
    Task<IReadOnlySet<(long TemplateId, int Month)>> GetExistingMonthsAsync(int year, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the owner's entries of a month, read-only, with their income template name.
    /// Names resolve even when the template has since been deactivated.
    /// </summary>
    Task<IReadOnlyList<IncomeEntryWithNameDto>> GetMonthWithNameAsync(int year, int month, long ownerId, CancellationToken cancellationToken);
}
