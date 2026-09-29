namespace Api.Contracts;

/// <summary>The request body for <c>PATCH /api/v1/entries/bill/{id}</c>.</summary>
/// <param name="PlannedAmount">The new planned amount (≥ 0), or <see langword="null"/> to keep it.</param>
/// <param name="ActualAmount">The new actual amount (≥ 0), or <see langword="null"/> to keep it.</param>
internal sealed record PatchBillEntryRequest(decimal? PlannedAmount, decimal? ActualAmount);

/// <summary>The (optional) request body for <c>POST /api/v1/entries/bill/{id}/pay</c>.</summary>
/// <param name="ActualAmount">The amount paid (≥ 0); defaults to the planned amount.</param>
/// <param name="PaidDate">The payment date (stored as midnight UTC); defaults to now.</param>
internal sealed record PayBillEntryRequest(decimal? ActualAmount, DateOnly? PaidDate);

/// <summary>The request body for <c>PATCH /api/v1/entries/income/{id}</c>.</summary>
/// <param name="PlannedAmount">The new planned amount (≥ 0), or <see langword="null"/> to keep it.</param>
/// <param name="ActualAmount">The new actual amount (≥ 0), or <see langword="null"/> to keep it.</param>
internal sealed record PatchIncomeEntryRequest(decimal? PlannedAmount, decimal? ActualAmount);

/// <summary>The (optional) request body for <c>POST /api/v1/entries/income/{id}/receive</c>.</summary>
/// <param name="ActualAmount">The amount received (≥ 0); defaults to the planned amount.</param>
/// <param name="ReceivedDate">The receipt date (stored as midnight UTC); defaults to now.</param>
internal sealed record ReceiveIncomeEntryRequest(decimal? ActualAmount, DateOnly? ReceivedDate);

/// <summary>The request body for <c>POST /api/v1/entries/bill</c>.</summary>
/// <param name="BillId">The one_off bill template to create an entry from.</param>
/// <param name="Year">The reference year (2000–2100).</param>
/// <param name="Month">The reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount (≥ 0); falls back to the template's DefaultAmount when null.</param>
internal sealed record CreateBillEntryRequest(long BillId, int Year, int Month, decimal? PlannedAmount);

/// <summary>The request body for <c>POST /api/v1/entries/income</c>.</summary>
/// <param name="IncomeId">The one_off income template to create an entry from.</param>
/// <param name="Year">The reference year (2000–2100).</param>
/// <param name="Month">The reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount (≥ 0); falls back to the template's DefaultAmount when null.</param>
internal sealed record CreateIncomeEntryRequest(long IncomeId, int Year, int Month, decimal? PlannedAmount);

/// <summary>
/// The bill entry payload returned by <c>/api/v1/receivables/{entryId}/mark|unmark</c>. Same shape as
/// <see cref="Application.DTOs.Services.BillEntryDto"/>; kept until the receivables endpoints are migrated.
/// </summary>
internal sealed record BillEntryCreatedDto(
    long Id, long BillId, int RefYear, int RefMonth,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal SplitRatioSnapshot, long? PersonId,
    bool Paid, DateTimeOffset? PaidDate,
    bool Received, DateTimeOffset? ReceivedDate);
