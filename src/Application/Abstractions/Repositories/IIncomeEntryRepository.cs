using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IIncomeEntryRepository
{
    void AddRange(IEnumerable<IncomeEntry> entries);

    /// <summary>Returns the (income id, month) pairs that already have an entry in the given year.</summary>
    Task<IReadOnlySet<(long TemplateId, int Month)>> GetExistingMonthsAsync(int year, CancellationToken cancellationToken);
}
