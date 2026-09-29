using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

/// <summary>Read side of the monthly entries: listing with derived values, totals and balances.</summary>
public interface IEntryService
{
    Task<Result<MonthEntriesDto>> GetMonthAsync(int? year, int? month, CancellationToken cancellationToken);
}
