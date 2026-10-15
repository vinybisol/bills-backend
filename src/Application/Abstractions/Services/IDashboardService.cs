using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

/// <summary>
/// Read-only dashboards over the owner's bill and income entries: a month summary with a
/// per-category breakdown, and a 12-month year view.
/// </summary>
public interface IDashboardService
{
    Task<Result<DashboardMonthDto>> GetMonthAsync(int? year, int? month, CancellationToken cancellationToken);

    /// <summary>Always returns 12 month rows (zeroed when a month has no data).</summary>
    Task<Result<DashboardYearDto>> GetYearAsync(int? year, CancellationToken cancellationToken);
}
