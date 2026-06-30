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
}
