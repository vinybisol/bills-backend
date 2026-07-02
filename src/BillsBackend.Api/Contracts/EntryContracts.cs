namespace BillsBackend.Api.Contracts;

/// <summary>The payload for a single bill entry returned by <c>GET /api/entries</c>.</summary>
/// <param name="Id">The bill entry id.</param>
/// <param name="BillId">The source bill template id.</param>
/// <param name="Name">The bill name (from the template at projection time).</param>
/// <param name="Category">The category name (from the template at projection time).</param>
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
internal sealed record BillEntryDto(long Id, long BillId, string Name, string Category,
    decimal PlannedAmount, decimal? ActualAmount, decimal SplitRatio, string? Person,
    decimal EffectiveAmount, decimal MyShare, decimal Receivable,
    bool Paid, DateTimeOffset? PaidDate, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>The payload for a single income entry returned by <c>GET /api/entries</c>.</summary>
/// <param name="Id">The income entry id.</param>
/// <param name="IncomeId">The source income template id.</param>
/// <param name="Name">The income name (from the template at projection time).</param>
/// <param name="PlannedAmount">The snapshotted planned amount.</param>
/// <param name="ActualAmount">The confirmed actual amount, or <see langword="null"/> when not yet set.</param>
/// <param name="EffectiveAmount">Actual when present; otherwise planned.</param>
/// <param name="Received">Whether this income has been received.</param>
/// <param name="ReceivedDate">The UTC instant the income was received, or <see langword="null"/>.</param>
internal sealed record IncomeEntryDto(long Id, long IncomeId, string Name,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal EffectiveAmount, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>Aggregated totals for the requested month, returned by <c>GET /api/entries</c>.</summary>
/// <param name="BillsPlanned">Sum of all bill entry planned amounts (full value, not myShare).</param>
/// <param name="BillsEffective">Sum of all bill entry effective amounts (full value, not myShare).</param>
/// <param name="MyShare">Sum of the owner's share across all bill entries.</param>
/// <param name="Receivable">
/// Alias of <see cref="ReceivablePending"/>, kept for backward compatibility: sum of the other person's
/// share across bill entries not yet marked as received (pending only).
/// </param>
/// <param name="Received">
/// Alias of <see cref="ReceivableReceived"/>, kept for backward compatibility: sum of the other person's
/// share across bill entries already marked as received.
/// <c>Received + Receivable</c> equals the total split amount owed by other people this month.
/// </param>
/// <param name="ReceivablePending">
/// Sum of the other person's share across bill entries not yet marked as received (pending only) — the
/// amount the owner is exposed to if it is never paid back. Same value as <see cref="Receivable"/>.
/// </param>
/// <param name="ReceivableReceived">
/// Sum of the other person's share across bill entries already marked as received. Same value as
/// <see cref="Received"/>.
/// </param>
/// <param name="PaidFull">
/// Sum of the full effective amount (not myShare) of bill entries already marked as paid — the actual
/// cash that left the owner's account, including the portion owed back by another person.
/// </param>
/// <param name="IncomesPlanned">Sum of all income entry planned amounts.</param>
/// <param name="IncomesEffective">Sum of all income entry effective amounts.</param>
/// <param name="SaldoPrevisto">
/// Alias of <see cref="SaldoPrevistoOtimista"/>, kept for backward compatibility.
/// </param>
/// <param name="SaldoReal">
/// <b>Behavior change:</b> now an alias of <see cref="SaldoRealizado"/> (received incomes and reimbursements
/// minus the full paid amount of bills). Previously this summed received incomes minus only the owner's
/// share of paid bills; that undercounted actual cash outflow when a bill was shared. Prefer
/// <see cref="SaldoRealizado"/> going forward.
/// </param>
/// <param name="SaldoPrevistoOtimista">
/// Optimistic planned balance, assuming everyone pays what they owe: Σ(income planned) − Σ(bill planned ×
/// split ratio). Same value as <see cref="SaldoPrevisto"/>.
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
internal sealed record MonthTotalsDto(decimal BillsPlanned, decimal BillsEffective,
    decimal MyShare, decimal Receivable, decimal Received,
    decimal ReceivablePending, decimal ReceivableReceived, decimal PaidFull,
    decimal IncomesPlanned, decimal IncomesEffective,
    decimal SaldoPrevisto, decimal SaldoReal,
    decimal SaldoPrevistoOtimista, decimal SaldoPrevistoPiorCaso, decimal SaldoRealizado);

/// <summary>The complete response returned by <c>GET /api/entries</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="Bills">Bill entries for the month, sorted by category then name.</param>
/// <param name="Incomes">Income entries for the month.</param>
/// <param name="Totals">Aggregated totals for the month.</param>
internal sealed record MonthEntriesDto(int Year, int Month,
    IReadOnlyList<BillEntryDto> Bills, IReadOnlyList<IncomeEntryDto> Incomes,
    MonthTotalsDto Totals);

/// <summary>The request body for <c>PATCH /api/entries/bill/{id}</c>.</summary>
internal sealed record PatchBillEntryRequest(decimal? PlannedAmount, decimal? ActualAmount);

/// <summary>The request body for <c>POST /api/entries/bill/{id}/pay</c>.</summary>
internal sealed record PayBillEntryRequest(decimal? ActualAmount, DateOnly? PaidDate);

/// <summary>The request body for <c>PATCH /api/entries/income/{id}</c>.</summary>
internal sealed record PatchIncomeEntryRequest(decimal? PlannedAmount, decimal? ActualAmount);

/// <summary>The request body for <c>POST /api/entries/income/{id}/receive</c>.</summary>
internal sealed record ReceiveIncomeEntryRequest(decimal? ActualAmount, DateOnly? ReceivedDate);

/// <summary>The request body for <c>POST /api/entries/bill</c>.</summary>
/// <param name="BillId">The one_off bill template to create an entry from.</param>
/// <param name="Year">The reference year.</param>
/// <param name="Month">The reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount; falls back to the template's DefaultAmount when null.</param>
internal sealed record CreateBillEntryRequest(long BillId, int Year, int Month, decimal? PlannedAmount);

/// <summary>The request body for <c>POST /api/entries/income</c>.</summary>
/// <param name="IncomeId">The one_off income template to create an entry from.</param>
/// <param name="Year">The reference year.</param>
/// <param name="Month">The reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount; falls back to the template's DefaultAmount when null.</param>
internal sealed record CreateIncomeEntryRequest(long IncomeId, int Year, int Month, decimal? PlannedAmount);

/// <summary>The payload returned by <c>POST /api/entries/bill</c>.</summary>
internal sealed record BillEntryCreatedDto(
    long Id, long BillId, int RefYear, int RefMonth,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal SplitRatioSnapshot, long? PersonId,
    bool Paid, DateTimeOffset? PaidDate,
    bool Received, DateTimeOffset? ReceivedDate);

/// <summary>The payload returned by <c>POST /api/entries/income</c>.</summary>
internal sealed record IncomeEntryCreatedDto(
    long Id, long IncomeId, int RefYear, int RefMonth,
    decimal PlannedAmount, decimal? ActualAmount,
    bool Received, DateTimeOffset? ReceivedDate);
