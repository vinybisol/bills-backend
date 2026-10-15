using Domain.Entities;

namespace Application.DTOs.Services;

/// <summary>A bill entry of the month listing, enriched with names and derived values.</summary>
/// <param name="Id">The bill entry id.</param>
/// <param name="BillId">The source bill template id.</param>
/// <param name="Name">The bill template name (resolved even if the template was deactivated).</param>
/// <param name="Category">The category name (resolved even if the category was deactivated).</param>
/// <param name="PlannedAmount">The snapshotted planned amount.</param>
/// <param name="ActualAmount">The confirmed actual amount, or <see langword="null"/> when not yet set.</param>
/// <param name="SplitRatio">The snapshotted owner split ratio.</param>
/// <param name="Person">The name of the person who owes the remaining fraction, or <see langword="null"/> when SplitRatio is 1.</param>
/// <param name="EffectiveAmount">Actual when present; otherwise planned.</param>
/// <param name="MyShare">Effective amount multiplied by the split ratio.</param>
/// <param name="Receivable">Effective amount multiplied by (1 − split ratio).</param>
/// <param name="Paid">Whether the owner has paid this bill.</param>
/// <param name="PaidDate">The UTC instant of payment, or <see langword="null"/>.</param>
/// <param name="Received">Whether the split portion has been received from the other person.</param>
/// <param name="ReceivedDate">The UTC instant the split was received, or <see langword="null"/>.</param>
public sealed record BillEntryListItemDto(long Id, long BillId, string Name, string Category,
    decimal PlannedAmount, decimal? ActualAmount, decimal SplitRatio, string? Person,
    decimal EffectiveAmount, decimal MyShare, decimal Receivable,
    bool Paid, DateTimeOffset? PaidDate, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>An income entry of the month listing, enriched with the template name and derived values.</summary>
/// <param name="Id">The income entry id.</param>
/// <param name="IncomeId">The source income template id.</param>
/// <param name="Name">The income template name (resolved even if the template was deactivated).</param>
/// <param name="PlannedAmount">The snapshotted planned amount.</param>
/// <param name="ActualAmount">The confirmed actual amount, or <see langword="null"/> when not yet set.</param>
/// <param name="EffectiveAmount">Actual when present; otherwise planned.</param>
/// <param name="Received">Whether this income has been received.</param>
/// <param name="ReceivedDate">The UTC instant the income was received, or <see langword="null"/>.</param>
public sealed record IncomeEntryListItemDto(long Id, long IncomeId, string Name,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal EffectiveAmount, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>Aggregated totals of a month, returned by <c>GET /api/v1/entries</c>.</summary>
/// <param name="BillsPlanned">Sum of all bill entry planned amounts (full value, not myShare).</param>
/// <param name="BillsEffective">Sum of all bill entry effective amounts (full value, not myShare).</param>
/// <param name="MyShare">Sum of the owner's share across all bill entries.</param>
/// <param name="Receivable">Alias of <see cref="ReceivablePending"/>, kept for backward compatibility.</param>
/// <param name="Received">Alias of <see cref="ReceivableReceived"/>, kept for backward compatibility.</param>
/// <param name="ReceivablePending">The other person's share across bill entries not yet received back.</param>
/// <param name="ReceivableReceived">The other person's share across bill entries already received back.</param>
/// <param name="PaidFull">Full effective amount (not myShare) of paid bill entries — the cash that actually left.</param>
/// <param name="IncomesPlanned">Sum of all income entry planned amounts.</param>
/// <param name="IncomesEffective">Sum of all income entry effective amounts, received or not.</param>
/// <param name="IncomesReceived">Sum of effective amounts of received income entries only.</param>
/// <param name="SaldoPrevisto">Alias of <see cref="SaldoPrevistoOtimista"/>, kept for backward compatibility.</param>
/// <param name="SaldoReal">Alias of <see cref="SaldoRealizado"/>, kept for backward compatibility.</param>
/// <param name="SaldoPrevistoOtimista">Σ(income planned) − Σ(bill planned × split ratio).</param>
/// <param name="SaldoPrevistoPiorCaso"><see cref="SaldoPrevistoOtimista"/> − <see cref="ReceivablePending"/>.</param>
/// <param name="SaldoRealizado">(received incomes + received reimbursements) − <see cref="PaidFull"/>.</param>
public sealed record MonthTotalsDto(decimal BillsPlanned, decimal BillsEffective,
    decimal MyShare, decimal Receivable, decimal Received,
    decimal ReceivablePending, decimal ReceivableReceived, decimal PaidFull,
    decimal IncomesPlanned, decimal IncomesEffective, decimal IncomesReceived,
    decimal SaldoPrevisto, decimal SaldoReal,
    decimal SaldoPrevistoOtimista, decimal SaldoPrevistoPiorCaso, decimal SaldoRealizado);

/// <summary>The complete response of <c>GET /api/v1/entries</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="Bills">Bill entries of the month, sorted by category then name.</param>
/// <param name="Incomes">Income entries of the month.</param>
/// <param name="Totals">Aggregated totals of the month.</param>
public sealed record MonthEntriesDto(int Year, int Month,
    IReadOnlyList<BillEntryListItemDto> Bills, IReadOnlyList<IncomeEntryListItemDto> Incomes,
    MonthTotalsDto Totals);

/// <summary>The payload returned by bill entry commands (create, patch, pay, unpay).</summary>
public sealed record BillEntryDto(
    long Id, long BillId, int RefYear, int RefMonth,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal SplitRatioSnapshot, long? PersonId,
    bool Paid, DateTimeOffset? PaidDate,
    bool Received, DateTimeOffset? ReceivedDate)
{
    public static BillEntryDto From(BillEntry e) => new(
        e.Id, e.BillId, e.RefYear, e.RefMonth,
        e.PlannedAmount, e.ActualAmount, e.SplitRatioSnapshot, e.PersonId,
        e.Paid, e.PaidDate, e.Received, e.ReceivedDate);
}

/// <summary>The payload returned by income entry commands (create, patch, receive, unreceive).</summary>
public sealed record IncomeEntryDto(
    long Id, long IncomeId, int RefYear, int RefMonth,
    decimal PlannedAmount, decimal? ActualAmount,
    bool Received, DateTimeOffset? ReceivedDate)
{
    public static IncomeEntryDto From(IncomeEntry e) => new(
        e.Id, e.IncomeId, e.RefYear, e.RefMonth,
        e.PlannedAmount, e.ActualAmount, e.Received, e.ReceivedDate);
}

/// <summary>
/// Read-side row of a month's bill entry together with the names it is displayed with. Names are
/// resolved even when the template, category or person have since been deactivated.
/// </summary>
/// <param name="Entry">The bill entry.</param>
/// <param name="Name">The bill template name.</param>
/// <param name="CategoryId">The id of the template's category.</param>
/// <param name="Category">The category name.</param>
/// <param name="Person">The name of the person owing the split, or <see langword="null"/>.</param>
public sealed record BillEntryWithNamesDto(BillEntry Entry, string Name, long CategoryId, string Category, string? Person);

/// <summary>
/// Read-side row of a month's income entry together with its template name (resolved even when
/// the template has since been deactivated).
/// </summary>
public sealed record IncomeEntryWithNameDto(IncomeEntry Entry, string Name);
