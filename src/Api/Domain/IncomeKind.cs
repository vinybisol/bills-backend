namespace BillsBackend.Api.Domain;

/// <summary>
/// The allowed income kinds for an <see cref="Income"/> template.
/// </summary>
public enum IncomeKind
{
    /// <summary>A recurring income that repeats every month (e.g. salary, rent).</summary>
    Recurring,

    /// <summary>A one-off, non-repeating receipt (e.g. bonus, freelance payment).</summary>
    OneOff
}
