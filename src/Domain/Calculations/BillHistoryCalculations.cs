using Domain.Entities;

namespace Domain.Calculations;

/// <summary>One month of a bill's history, with derived values.</summary>
public sealed record BillHistoryItem(
    int Year, int Month,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal Effective, decimal MyShare,
    bool Paid, DateTimeOffset? PaidDate,
    EntryCalculations.Variation? Variation);

/// <summary>Aggregates over a bill's history.</summary>
public readonly record struct BillHistorySummary(decimal AverageEffective, decimal MinEffective, decimal MaxEffective, decimal TotalPaidMyShare);

/// <summary>Pure computation of a bill's chronological history.</summary>
public static class BillHistoryCalculations
{
    /// <summary>
    /// Orders entries chronologically and computes, for each one, the effective amount, the owner's
    /// share and the variation relative to the previous month's effective amount.
    /// </summary>
    public static IReadOnlyList<BillHistoryItem> BuildItems(IEnumerable<BillEntry> entries)
    {
        var items = new List<BillHistoryItem>();
        decimal? previousEffective = null;

        foreach (var e in entries.OrderBy(e => e.RefYear).ThenBy(e => e.RefMonth))
        {
            var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
            items.Add(new BillHistoryItem(
                e.RefYear, e.RefMonth, e.PlannedAmount, e.ActualAmount, effective,
                EntryCalculations.MyShare(effective, e.SplitRatioSnapshot),
                e.Paid, e.PaidDate,
                EntryCalculations.ComputeVariation(effective, previousEffective)));

            previousEffective = effective;
        }

        return items;
    }

    /// <summary>
    /// Summarizes history items: average/min/max effective amount (zero when empty) and the
    /// owner's share of paid items.
    /// </summary>
    public static BillHistorySummary Summarize(IReadOnlyCollection<BillHistoryItem> items) =>
        new(
            items.Count > 0 ? items.Average(i => i.Effective) : 0m,
            items.Count > 0 ? items.Min(i => i.Effective) : 0m,
            items.Count > 0 ? items.Max(i => i.Effective) : 0m,
            items.Where(i => i.Paid).Sum(i => i.MyShare));
}
