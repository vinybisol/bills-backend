namespace BillsBackend.Api.Domain;

/// <summary>
/// The allowed bill kinds for a <see cref="Bill"/> template.
/// </summary>
public enum BillKind
{
    /// <summary>A recurring expense that repeats every month (e.g. rent, subscription).</summary>
    Recurring,

    /// <summary>A one-off, non-repeating expense (e.g. a repair, a purchase).</summary>
    OneOff
}
