namespace BillsBackend.Api.Domain;

/// <summary>
/// Represents a projected monthly entry for an income (lançamento de receita).
/// </summary>
/// <remarks>
/// <para>
/// Created when the annual projection is generated for a given year. Carries a snapshot of
/// <see cref="PlannedAmount"/> from the income template at projection time, so historical entries
/// are unaffected by later template edits.
/// </para>
/// State is encapsulated: create through <see cref="Create"/> and mutate through behavior methods.
/// </remarks>
public sealed class IncomeEntry
{
    private IncomeEntry() { }

    private IncomeEntry(
        long ownerId,
        long incomeId,
        int refYear,
        int refMonth,
        decimal plannedAmount,
        DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        IncomeId = incomeId;
        RefYear = refYear;
        RefMonth = refMonth;
        PlannedAmount = plannedAmount;
        Received = false;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the database-generated identifier.</summary>
    public long Id { get; private set; }

    /// <summary>Gets the internal <c>app_user.id</c> of the user who owns this entry.</summary>
    public long OwnerId { get; private set; }

    /// <summary>Gets the id of the income template this entry was generated from.</summary>
    public long IncomeId { get; private set; }

    /// <summary>Gets the reference year.</summary>
    public int RefYear { get; private set; }

    /// <summary>Gets the reference month (1–12).</summary>
    public int RefMonth { get; private set; }

    /// <summary>
    /// Gets the planned amount for this month, snapshotted from the income template at
    /// projection time.
    /// </summary>
    public decimal PlannedAmount { get; private set; }

    /// <summary>
    /// Gets the actual amount received for this month, or <see langword="null"/> if not yet confirmed.
    /// </summary>
    public decimal? ActualAmount { get; private set; }

    /// <summary>Gets whether this income has been received by the owner.</summary>
    public bool Received { get; private set; }

    /// <summary>Gets the UTC instant at which the income was received, or <see langword="null"/> when not yet received.</summary>
    public DateTimeOffset? ReceivedDate { get; private set; }

    /// <summary>Gets the UTC instant at which this entry was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a new income entry for the given owner, income template, year, and month.
    /// Snapshots <paramref name="plannedAmount"/> from the income template at the time of projection.
    /// </summary>
    /// <param name="ownerId">The internal user id; must be positive.</param>
    /// <param name="incomeId">The income template id; must be positive.</param>
    /// <param name="refYear">The reference year.</param>
    /// <param name="refMonth">The reference month (1–12).</param>
    /// <param name="plannedAmount">The planned amount; must be zero or greater.</param>
    /// <param name="createdAt">The UTC creation timestamp.</param>
    /// <returns>A new <see cref="IncomeEntry"/> with <see cref="Received"/> set to <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="ownerId"/> or <paramref name="incomeId"/> is not positive;
    ///   or <paramref name="plannedAmount"/> is negative.
    /// </exception>
    public static IncomeEntry Create(
        long ownerId,
        long incomeId,
        int refYear,
        int refMonth,
        decimal plannedAmount,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(incomeId);
        ArgumentOutOfRangeException.ThrowIfNegative(plannedAmount);

        return new IncomeEntry(ownerId, incomeId, refYear, refMonth, plannedAmount, createdAt);
    }

    /// <summary>
    /// Records that the owner has received this income.
    /// </summary>
    /// <param name="receivedAt">The UTC instant at which the income was received.</param>
    public void MarkReceived(DateTimeOffset receivedAt)
    {
        Received = true;
        ReceivedDate = receivedAt;
    }
}
