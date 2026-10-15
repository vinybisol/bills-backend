using Domain.Entities;

namespace Domain.Calculations;

/// <summary>Outcome of propagating a new amount to a bill's entries.</summary>
public readonly record struct RecalculationResult(int UpdatedEntries, int SkippedPaid);

/// <summary>Propagation of a bill template's new default amount to its entries.</summary>
public static class BillRecalculation
{
    /// <summary>
    /// Sets <paramref name="newAmount"/> as the planned amount of every unpaid entry; paid entries
    /// are frozen and skipped. The caller is responsible for selecting the entries in range.
    /// </summary>
    public static RecalculationResult ApplyToEntries(IEnumerable<BillEntry> entries, decimal newAmount)
    {
        int updated = 0, skippedPaid = 0;
        foreach (var entry in entries)
        {
            if (entry.Paid)
            {
                skippedPaid++;
                continue;
            }

            entry.UpdatePlanned(newAmount);
            updated++;
        }

        return new RecalculationResult(updated, skippedPaid);
    }
}
