namespace BillsBackend.Api.Contracts;

/// <summary>The per-category breakdown row returned by <c>GET /api/dashboard/month</c>.</summary>
/// <param name="CategoryId">The category id.</param>
/// <param name="Category">The category display name.</param>
/// <param name="PlannedMyShare">Sum of (planned amount × split ratio) over all bill entries in the category, regardless of paid status.</param>
/// <param name="ActualMyShare">Sum of (effective amount × split ratio) over only the paid bill entries in the category.</param>
/// <param name="Diff">
/// <see cref="ActualMyShare"/> minus <see cref="PlannedMyShare"/>. Positive means the category overspent relative to plan.
/// </param>
internal sealed record DashboardCategoryDto(long CategoryId, string Category, decimal PlannedMyShare, decimal ActualMyShare, decimal Diff);

/// <summary>Aggregated month-level totals returned by <c>GET /api/dashboard/month</c>.</summary>
/// <param name="PlannedExpense">Sum of the owner's share of planned amounts across all bill entries.</param>
/// <param name="ActualExpense">Sum of the owner's share of effective amounts across paid bill entries only.</param>
/// <param name="PlannedIncome">Sum of planned amounts across all income entries.</param>
/// <param name="ActualIncome">Sum of effective amounts across received income entries only.</param>
/// <param name="SaldoPrevisto"><see cref="PlannedIncome"/> minus <see cref="PlannedExpense"/>.</param>
/// <param name="SaldoReal"><see cref="ActualIncome"/> minus <see cref="ActualExpense"/>.</param>
/// <param name="BillsPaid">The number of bill entries in the month with <c>Paid</c> set to <see langword="true"/>.</param>
/// <param name="BillsTotal">The total number of bill entries in the month.</param>
/// <param name="IncomesReceived">The number of income entries in the month with <c>Received</c> set to <see langword="true"/>.</param>
/// <param name="IncomesTotal">The total number of income entries in the month.</param>
internal sealed record DashboardSummaryDto(
    decimal PlannedExpense, decimal ActualExpense,
    decimal PlannedIncome, decimal ActualIncome,
    decimal SaldoPrevisto, decimal SaldoReal,
    int BillsPaid, int BillsTotal,
    int IncomesReceived, int IncomesTotal);

/// <summary>The complete response returned by <c>GET /api/dashboard/month</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="Summary">Aggregated totals for the month.</param>
/// <param name="ByCategory">Per-category breakdown, ordered by <see cref="DashboardCategoryDto.PlannedMyShare"/> descending. Categories with no bill entries in the month are omitted.</param>
internal sealed record DashboardMonthDto(int Year, int Month, DashboardSummaryDto Summary, IReadOnlyList<DashboardCategoryDto> ByCategory);

/// <summary>A single month's summary row within <c>GET /api/dashboard/year</c>.</summary>
/// <param name="Month">The month (1–12).</param>
/// <param name="PlannedExpense">Sum of the owner's share of planned amounts across all bill entries in the month.</param>
/// <param name="ActualExpense">Sum of the owner's share of effective amounts across paid bill entries only.</param>
/// <param name="PlannedIncome">Sum of planned amounts across all income entries in the month.</param>
/// <param name="ActualIncome">Sum of effective amounts across received income entries only.</param>
/// <param name="SaldoPrevisto"><see cref="PlannedIncome"/> minus <see cref="PlannedExpense"/>.</param>
/// <param name="SaldoReal"><see cref="ActualIncome"/> minus <see cref="ActualExpense"/>.</param>
internal sealed record DashboardMonthSummaryDto(
    int Month, decimal PlannedExpense, decimal ActualExpense,
    decimal PlannedIncome, decimal ActualIncome, decimal SaldoPrevisto, decimal SaldoReal);

/// <summary>The per-category breakdown row returned by <c>GET /api/dashboard/year</c>, totalled over the whole year.</summary>
/// <param name="CategoryId">The category id.</param>
/// <param name="Category">The category display name.</param>
/// <param name="PlannedMyShare">Sum of (planned amount × split ratio) over the whole year, regardless of paid status.</param>
/// <param name="ActualMyShare">Sum of (effective amount × split ratio) over only the paid bill entries in the year.</param>
internal sealed record DashboardCategoryYearDto(long CategoryId, string Category, decimal PlannedMyShare, decimal ActualMyShare);

/// <summary>Aggregated year-level totals returned by <c>GET /api/dashboard/year</c> — the sum of the 12 months.</summary>
/// <param name="PlannedExpense">Sum of <see cref="DashboardMonthSummaryDto.PlannedExpense"/> across the 12 months.</param>
/// <param name="ActualExpense">Sum of <see cref="DashboardMonthSummaryDto.ActualExpense"/> across the 12 months.</param>
/// <param name="PlannedIncome">Sum of <see cref="DashboardMonthSummaryDto.PlannedIncome"/> across the 12 months.</param>
/// <param name="ActualIncome">Sum of <see cref="DashboardMonthSummaryDto.ActualIncome"/> across the 12 months.</param>
/// <param name="SaldoPrevisto"><see cref="PlannedIncome"/> minus <see cref="PlannedExpense"/>.</param>
/// <param name="SaldoReal"><see cref="ActualIncome"/> minus <see cref="ActualExpense"/>.</param>
internal sealed record DashboardYearTotalsDto(
    decimal PlannedExpense, decimal ActualExpense,
    decimal PlannedIncome, decimal ActualIncome, decimal SaldoPrevisto, decimal SaldoReal);

/// <summary>The complete response returned by <c>GET /api/dashboard/year</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Months">Always exactly 12 entries (month 1–12); months with no data are zeroed rather than omitted.</param>
/// <param name="ByCategory">
/// Whole-year per-category totals, ordered by <see cref="DashboardCategoryYearDto.PlannedMyShare"/> descending.
/// Categories with no bill entries in the year are omitted.
/// </param>
/// <param name="Totals">Grand totals for the year — the sum of the 12 months.</param>
internal sealed record DashboardYearDto(
    int Year, IReadOnlyList<DashboardMonthSummaryDto> Months,
    IReadOnlyList<DashboardCategoryYearDto> ByCategory, DashboardYearTotalsDto Totals);
