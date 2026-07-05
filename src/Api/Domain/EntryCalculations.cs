namespace BillsBackend.Api.Domain;

/// <summary>Purely functional helpers for computing derived entry values.</summary>
public static class EntryCalculations
{
    /// <summary>Returns actual when present, otherwise planned.</summary>
    public static decimal EffectiveAmount(decimal planned, decimal? actual) =>
        actual ?? planned;

    /// <summary>The owner's share of the effective amount.</summary>
    public static decimal MyShare(decimal effective, decimal splitRatio) =>
        effective * splitRatio;

    /// <summary>The amount owed to the owner by the other person.</summary>
    public static decimal Receivable(decimal effective, decimal splitRatio) =>
        effective * (1 - splitRatio);

    /// <summary>Returns true when the given period is at or after the specified start month.</summary>
    public static bool IsInForwardRange(int entryYear, int entryMonth, int fromYear, int fromMonth) =>
        entryYear > fromYear || (entryYear == fromYear && entryMonth >= fromMonth);

    /// <summary>The absolute and percentage change of a value relative to the previous period.</summary>
    /// <param name="Abs">The absolute difference: current minus previous.</param>
    /// <param name="Pct">
    /// The percentage difference relative to <c>previous</c>, or <see langword="null"/> when
    /// <c>previous</c> is zero (a percentage change would be undefined).
    /// </param>
    public readonly record struct Variation(decimal Abs, decimal? Pct);

    /// <summary>
    /// Computes the variation of <paramref name="current"/> relative to <paramref name="previous"/>.
    /// Returns <see langword="null"/> when there is no previous value — the first item in a
    /// chronological series has no variation to report.
    /// </summary>
    public static Variation? ComputeVariation(decimal current, decimal? previous)
    {
        if (previous is null)
            return null;

        var abs = current - previous.Value;
        var pct = previous.Value == 0m ? (decimal?)null : Math.Round(abs / previous.Value * 100m, 2);
        return new Variation(abs, pct);
    }
}
