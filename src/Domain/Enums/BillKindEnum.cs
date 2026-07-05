namespace Domain.Enums;

/// <summary>
/// The allowed bill kinds for a <see cref="Bill"/> template.
/// </summary>
public enum BillKindEnum
{
    /// <summary>A recurring expense that repeats every month (e.g. rent, subscription).</summary>
    Recurring,

    /// <summary>A one-off, non-repeating expense (e.g. a repair, a purchase).</summary>
    OneOff
}
