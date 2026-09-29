using Domain.Entities;

namespace Domain.Calculations;

/// <summary>The owner's planned and actual share of bill entries grouped under one category.</summary>
public readonly record struct CategoryShare(long CategoryId, decimal PlannedMyShare, decimal ActualMyShare);

/// <summary>Expense and income totals of a single month (1–12).</summary>
public readonly record struct MonthSummary(
    int Month, decimal PlannedExpense, decimal ActualExpense, decimal PlannedIncome, decimal ActualIncome)
{
    /// <summary>Planned income minus planned expense (owner's share).</summary>
    public decimal PlannedBalance => PlannedIncome - PlannedExpense;

    /// <summary>Received income minus paid expense (owner's share).</summary>
    public decimal ActualBalance => ActualIncome - ActualExpense;
}

/// <summary>Receivable totals: everything owed, the part already received and the part still pending.</summary>
public readonly record struct ReceivableTotals(decimal Total, decimal Received, decimal Pending);

/// <summary>Pure aggregations over collections of bill and income entries.</summary>
public static class EntryAggregations
{
    /// <summary>The owner's share of an entry's effective amount.</summary>
    public static decimal EffectiveMyShare(BillEntry e) =>
        EntryCalculations.MyShare(EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot);

    /// <summary>The other person's share of an entry's effective amount.</summary>
    public static decimal EffectiveReceivable(BillEntry e) =>
        EntryCalculations.Receivable(EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot);

    /// <summary>Sum of the owner's share of the planned amount, over all entries.</summary>
    public static decimal PlannedMyShare(IEnumerable<BillEntry> entries) =>
        entries.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));

    /// <summary>Sum of the owner's share of the effective amount, over paid entries only.</summary>
    public static decimal ActualMyShare(IEnumerable<BillEntry> entries) =>
        entries.Where(e => e.Paid).Sum(EffectiveMyShare);

    /// <summary>Sum of the other person's share not yet received back.</summary>
    public static decimal ReceivablePending(IEnumerable<BillEntry> entries) =>
        entries.Where(e => !e.Received).Sum(EffectiveReceivable);

    /// <summary>Sum of the other person's share already received back.</summary>
    public static decimal ReceivableReceived(IEnumerable<BillEntry> entries) =>
        entries.Where(e => e.Received).Sum(EffectiveReceivable);

    /// <summary>Full (not my share) effective amount of paid entries — the cash that actually left.</summary>
    public static decimal PaidFull(IEnumerable<BillEntry> entries) =>
        entries.Where(e => e.Paid).Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

    /// <summary>Sum of planned income amounts.</summary>
    public static decimal PlannedIncome(IEnumerable<IncomeEntry> entries) =>
        entries.Sum(e => e.PlannedAmount);

    /// <summary>Sum of effective income amounts, over received entries only.</summary>
    public static decimal ReceivedIncome(IEnumerable<IncomeEntry> entries) =>
        entries.Where(e => e.Received).Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

    /// <summary>Receivable totals (total / received / pending) of the given entries.</summary>
    public static ReceivableTotals SummarizeReceivables(IEnumerable<BillEntry> entries)
    {
        var list = entries as IReadOnlyCollection<BillEntry> ?? entries.ToList();
        return new ReceivableTotals(
            list.Sum(EffectiveReceivable),
            ReceivableReceived(list),
            ReceivablePending(list));
    }

    /// <summary>
    /// Filters entries by receivable status: <c>"received"</c> keeps received entries, <c>"pending"</c>
    /// keeps pending ones, anything else (including <see langword="null"/>) keeps all.
    /// </summary>
    public static IEnumerable<BillEntry> FilterByReceivedStatus(IEnumerable<BillEntry> entries, string? status) =>
        status switch
        {
            "received" => entries.Where(e => e.Received),
            "pending" => entries.Where(e => !e.Received),
            _ => entries,
        };

    /// <summary>
    /// Groups bill entries by category and sums the owner's planned share (all entries) and actual
    /// share (paid entries only). Ordered by planned share descending; categories without entries
    /// are absent.
    /// </summary>
    public static IReadOnlyList<CategoryShare> SummarizeByCategory(
        IEnumerable<BillEntry> entries, Func<BillEntry, long> categoryOf) =>
        entries
            .GroupBy(categoryOf)
            .Select(g => new CategoryShare(g.Key, PlannedMyShare(g), ActualMyShare(g)))
            .OrderByDescending(c => c.PlannedMyShare)
            .ToList();

    /// <summary>
    /// Builds 12 always-present month summaries (1..12, zeroed when there is no data) from the
    /// bill and income entries of a year.
    /// </summary>
    public static IReadOnlyList<MonthSummary> SummarizeByMonth(
        IEnumerable<BillEntry> billEntries, IEnumerable<IncomeEntry> incomeEntries)
    {
        var billsByMonth = billEntries.ToLookup(e => e.RefMonth);
        var incomesByMonth = incomeEntries.ToLookup(e => e.RefMonth);

        return Enumerable.Range(1, 12)
            .Select(m => new MonthSummary(
                m,
                PlannedMyShare(billsByMonth[m]),
                ActualMyShare(billsByMonth[m]),
                PlannedIncome(incomesByMonth[m]),
                ReceivedIncome(incomesByMonth[m])))
            .ToList();
    }
}
