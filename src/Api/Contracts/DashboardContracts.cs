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
/// <param name="SaldoPrevisto">
/// Alias of <see cref="SaldoPrevistoOtimista"/>, kept for backward compatibility.
/// </param>
/// <param name="SaldoReal">
/// <b>Behavior change:</b> now an alias of <see cref="SaldoRealizado"/> (received incomes and reimbursements
/// minus the full paid amount of bills). Previously this equalled <see cref="ActualIncome"/> minus
/// <see cref="ActualExpense"/> (the owner's share of paid bills only), which undercounted actual cash outflow
/// when a bill was shared. Prefer <see cref="SaldoRealizado"/> going forward.
/// </param>
/// <param name="BillsPaid">The number of bill entries in the month with <c>Paid</c> set to <see langword="true"/>.</param>
/// <param name="BillsTotal">The total number of bill entries in the month.</param>
/// <param name="IncomesReceived">The number of income entries in the month with <c>Received</c> set to <see langword="true"/>.</param>
/// <param name="IncomesTotal">The total number of income entries in the month.</param>
/// <param name="ReceivablePending">
/// Sum of the other person's share of bill entries not yet marked as received — the amount the owner is
/// exposed to if it is never paid back.
/// </param>
/// <param name="ReceivableReceived">Sum of the other person's share of bill entries already marked as received.</param>
/// <param name="PaidFull">
/// Sum of the full effective amount (not myShare) of bill entries already marked as paid — the actual cash
/// that left the owner's account, including the portion owed back by another person.
/// </param>
/// <param name="SaldoPrevistoOtimista">
/// Optimistic planned balance, assuming everyone pays what they owe: <see cref="PlannedIncome"/> minus
/// <see cref="PlannedExpense"/>. Same value as <see cref="SaldoPrevisto"/>.
/// </param>
/// <param name="SaldoPrevistoPiorCaso">
/// Worst-case planned balance: <see cref="SaldoPrevistoOtimista"/> minus <see cref="ReceivablePending"/> —
/// assumes the pending receivable is never paid back. <c>SaldoPrevistoOtimista − SaldoPrevistoPiorCaso</c>
/// always equals <see cref="ReceivablePending"/>.
/// </param>
/// <param name="SaldoRealizado">
/// Actual realised cash balance: (received incomes + received reimbursements) minus the full amount
/// actually paid for bills (not just the owner's share). Same value as <see cref="SaldoReal"/>.
/// </param>
internal sealed record DashboardSummaryDto(
    decimal PlannedExpense, decimal ActualExpense,
    decimal PlannedIncome, decimal ActualIncome,
    decimal SaldoPrevisto, decimal SaldoReal,
    int BillsPaid, int BillsTotal,
    int IncomesReceived, int IncomesTotal,
    decimal ReceivablePending, decimal ReceivableReceived, decimal PaidFull,
    decimal SaldoPrevistoOtimista, decimal SaldoPrevistoPiorCaso, decimal SaldoRealizado);

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
