namespace BillsBackend.Api.Domain;

/// <summary>
/// Represents a projected monthly entry for a bill (lançamento de despesa).
/// </summary>
/// <remarks>
/// <para>
/// Created when the annual projection is generated for a given year. Carries a snapshot of
/// <see cref="PlannedAmount"/> and <see cref="SplitRatioSnapshot"/> from the bill template at
/// projection time, so historical entries are unaffected by later template edits.
/// </para>
/// <para>
/// The <see cref="PersonId"/> field records who owes the remaining fraction when
/// <see cref="SplitRatioSnapshot"/> is less than 1. <see cref="Received"/> and
/// <see cref="ReceivedDate"/> track whether that portion was received from the other person.
/// </para>
/// State is encapsulated: create through <see cref="Create"/> and mutate through behavior methods.
/// </remarks>
public sealed class BillEntry
{
    private BillEntry() { }

    private BillEntry(
        long ownerId,
        long billId,
        int refYear,
        int refMonth,
        decimal plannedAmount,
        decimal splitRatioSnapshot,
        long? personId,
        DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        BillId = billId;
        RefYear = refYear;
        RefMonth = refMonth;
        PlannedAmount = plannedAmount;
        SplitRatioSnapshot = splitRatioSnapshot;
        PersonId = personId;
        Paid = false;
        Received = false;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the database-generated identifier.</summary>
    public long Id { get; private set; }

    /// <summary>Gets the internal <c>app_user.id</c> of the user who owns this entry.</summary>
    public long OwnerId { get; private set; }

    /// <summary>Gets the id of the bill template this entry was generated from.</summary>
    public long BillId { get; private set; }

    /// <summary>Gets the reference year.</summary>
    public int RefYear { get; private set; }

    /// <summary>Gets the reference month (1–12).</summary>
    public int RefMonth { get; private set; }

    /// <summary>
    /// Gets the planned amount for this month, snapshotted from the bill template at
    /// projection time.
    /// </summary>
    public decimal PlannedAmount { get; private set; }

    /// <summary>
    /// Gets the actual amount paid for this month, or <see langword="null"/> if not yet confirmed.
    /// </summary>
    public decimal? ActualAmount { get; private set; }

    /// <summary>
    /// Gets the split ratio snapshotted from the bill template at projection time.
    /// </summary>
    public decimal SplitRatioSnapshot { get; private set; }

    /// <summary>
    /// Gets the person who owes the remaining fraction, snapshotted from the bill template.
    /// <see langword="null"/> when <see cref="SplitRatioSnapshot"/> is exactly 1.
    /// </summary>
    public long? PersonId { get; private set; }

    /// <summary>Gets whether this expense has been paid by the owner.</summary>
    public bool Paid { get; private set; }

    /// <summary>Gets the UTC instant at which the expense was paid, or <see langword="null"/> when not yet paid.</summary>
    public DateTimeOffset? PaidDate { get; private set; }

    /// <summary>Gets whether the split portion has been received from the other person.</summary>
    public bool Received { get; private set; }

    /// <summary>Gets the UTC instant at which the split portion was received, or <see langword="null"/>.</summary>
    public DateTimeOffset? ReceivedDate { get; private set; }

    /// <summary>Gets the UTC instant at which this entry was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a new bill entry for the given owner, bill template, year, and month.
    /// Snapshots <paramref name="plannedAmount"/>, <paramref name="splitRatioSnapshot"/>,
    /// and <paramref name="personId"/> from the bill template at the time of projection.
    /// </summary>
    /// <param name="ownerId">The internal user id; must be positive.</param>
    /// <param name="billId">The bill template id; must be positive.</param>
    /// <param name="refYear">The reference year.</param>
    /// <param name="refMonth">The reference month (1–12).</param>
    /// <param name="plannedAmount">The planned amount; must be zero or greater.</param>
    /// <param name="splitRatioSnapshot">The split ratio at projection time.</param>
    /// <param name="personId">The person who owes the remaining fraction, or <see langword="null"/>.</param>
    /// <param name="createdAt">The UTC creation timestamp.</param>
    /// <returns>A new <see cref="BillEntry"/> with <see cref="Paid"/> and <see cref="Received"/> set to <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="ownerId"/> or <paramref name="billId"/> is not positive;
    ///   or <paramref name="plannedAmount"/> is negative.
    /// </exception>
    public static BillEntry Create(
        long ownerId,
        long billId,
        int refYear,
        int refMonth,
        decimal plannedAmount,
        decimal splitRatioSnapshot,
        long? personId,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(billId);
        ArgumentOutOfRangeException.ThrowIfNegative(plannedAmount);

        return new BillEntry(ownerId, billId, refYear, refMonth, plannedAmount, splitRatioSnapshot, personId, createdAt);
    }

    /// <summary>
    /// Records that the owner has paid this expense, optionally recording the actual amount paid.
    /// If <paramref name="actualAmount"/> is <see langword="null"/>, <see cref="PlannedAmount"/> is used as the actual amount.
    /// </summary>
    /// <param name="paidAt">The UTC instant at which the payment was made.</param>
    /// <param name="actualAmount">The actual amount paid, or <see langword="null"/> to default to the planned amount.</param>
    public void MarkPaid(DateTimeOffset paidAt, decimal? actualAmount = null)
    {
        ActualAmount = actualAmount ?? PlannedAmount;
        Paid = true;
        PaidDate = paidAt;
    }

    /// <summary>
    /// Reverses a prior <see cref="MarkPaid"/> call, unfreezing this entry for editing.
    /// </summary>
    public void Unfreeze()
    {
        Paid = false;
        PaidDate = null;
    }

    /// <summary>
    /// Updates the planned and/or actual amounts for this entry.
    /// </summary>
    /// <param name="plannedAmount">New planned amount, or <see langword="null"/> to leave unchanged.</param>
    /// <param name="actualAmount">New actual amount, or <see langword="null"/> to leave unchanged.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either amount is negative.</exception>
    public void UpdateAmounts(decimal? plannedAmount, decimal? actualAmount)
    {
        if (plannedAmount.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(plannedAmount.Value);
            PlannedAmount = plannedAmount.Value;
        }
        if (actualAmount.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(actualAmount.Value);
            ActualAmount = actualAmount.Value;
        }
    }

    /// <summary>
    /// Updates the planned amount for recalculation. Only call on unpaid entries.
    /// </summary>
    /// <param name="newAmount">The new planned amount; must be zero or greater.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="newAmount"/> is negative.</exception>
    public void UpdatePlanned(decimal newAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newAmount);
        PlannedAmount = newAmount;
    }

    /// <summary>
    /// Records that the split portion has been received from the other person.
    /// </summary>
    /// <param name="receivedAt">The UTC instant at which the portion was received.</param>
    public void MarkReceived(DateTimeOffset receivedAt)
    {
        Received = true;
        ReceivedDate = receivedAt;
    }

    /// <summary>
    /// Reverses a prior <see cref="MarkReceived"/> call, clearing <see cref="Received"/> and
    /// <see cref="ReceivedDate"/>. Does not touch <see cref="Paid"/> or <see cref="PaidDate"/>:
    /// those track the independent fact that the owner paid the bill, whereas
    /// <see cref="Received"/> tracks whether the other person paid back their split.
    /// </summary>
    public void UnmarkReceived()
    {
        Received = false;
        ReceivedDate = null;
    }
}
