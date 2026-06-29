namespace BillsBackend.Api.Domain;

/// <summary>
/// Represents a bill template (molde de despesa) in the owner's financial plan, with optional split.
/// </summary>
/// <remarks>
/// Bills are soft-deleted via <see cref="Deactivate"/> — <see cref="Active"/> is set to
/// <see langword="false"/> and the row is never physically removed.
/// Names are <em>not</em> unique per owner: the same bill name may appear multiple times.
/// <para>
/// When <see cref="SplitRatio"/> is less than 1, <see cref="PersonId"/> is required and identifies
/// who owes the remaining fraction. When <see cref="SplitRatio"/> is exactly 1, the expense is fully
/// the owner's and <see cref="PersonId"/> must be <see langword="null"/>.
/// </para>
/// State is encapsulated: create through <see cref="Create"/> and mutate through behavior methods only.
/// </remarks>
public sealed class Bill
{
    private Bill() { }

    private Bill(
        long ownerId,
        string name,
        long categoryId,
        BillKind kind,
        decimal defaultAmount,
        decimal splitRatio,
        long? personId,
        DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        Name = name;
        CategoryId = categoryId;
        Kind = kind;
        DefaultAmount = defaultAmount;
        SplitRatio = splitRatio;
        PersonId = personId;
        Active = true;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the database-generated identifier.</summary>
    public long Id { get; private set; }

    /// <summary>Gets the internal <c>app_user.id</c> of the user who owns this bill template.</summary>
    public long OwnerId { get; private set; }

    /// <summary>Gets the display name of the bill template.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Gets the category this bill belongs to.</summary>
    public long CategoryId { get; private set; }

    /// <summary>Gets the bill kind: recurring or one-off.</summary>
    public BillKind Kind { get; private set; }

    /// <summary>Gets the default planned amount for this bill template. Zero or greater.</summary>
    public decimal DefaultAmount { get; private set; }

    /// <summary>
    /// Gets the fraction of the bill that is the owner's responsibility.
    /// 1.0 means entirely the owner's; values less than 1 indicate a shared expense.
    /// Must be in the range [0, 1].
    /// </summary>
    public decimal SplitRatio { get; private set; }

    /// <summary>
    /// Gets the person who owes the remaining fraction when <see cref="SplitRatio"/> is less than 1.
    /// Must be <see langword="null"/> when <see cref="SplitRatio"/> is exactly 1.
    /// </summary>
    public long? PersonId { get; private set; }

    /// <summary>
    /// Gets whether the bill template is active.
    /// <see langword="false"/> means it has been soft-deleted and must not appear in listings.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>Gets the UTC instant at which the bill template was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a new active bill template for the given owner.
    /// </summary>
    /// <param name="ownerId">The internal user id; must be positive.</param>
    /// <param name="name">The bill name; trimmed, must not be blank.</param>
    /// <param name="categoryId">The category this bill belongs to.</param>
    /// <param name="kind">The bill kind.</param>
    /// <param name="defaultAmount">The default planned amount; must be zero or greater.</param>
    /// <param name="splitRatio">The owner's fraction of the expense; must be in [0, 1].</param>
    /// <param name="personId">
    ///   Required when <paramref name="splitRatio"/> is less than 1;
    ///   must be <see langword="null"/> when <paramref name="splitRatio"/> is exactly 1.
    /// </param>
    /// <param name="createdAt">The UTC creation timestamp.</param>
    /// <returns>A new active <see cref="Bill"/>.</returns>
    /// <exception cref="ArgumentException">
    ///   <paramref name="name"/> is null, empty, or whitespace;
    ///   or the split/person combination is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="ownerId"/> is not positive;
    ///   <paramref name="defaultAmount"/> is negative;
    ///   or <paramref name="splitRatio"/> is outside [0, 1].
    /// </exception>
    public static Bill Create(
        long ownerId,
        string name,
        long categoryId,
        BillKind kind,
        decimal defaultAmount,
        decimal splitRatio,
        long? personId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultAmount);

        if (splitRatio < 0m || splitRatio > 1m)
            throw new ArgumentOutOfRangeException(nameof(splitRatio), "SplitRatio must be between 0 and 1.");

        ValidateSplitPersonRule(splitRatio, personId);

        return new Bill(ownerId, name.Trim(), categoryId, kind, defaultAmount, splitRatio, personId, createdAt);
    }

    /// <summary>
    /// Updates all editable fields of the bill template.
    /// </summary>
    /// <param name="name">The new name; trimmed, must not be blank.</param>
    /// <param name="categoryId">The new category.</param>
    /// <param name="kind">The new kind.</param>
    /// <param name="defaultAmount">The new default amount; must be zero or greater.</param>
    /// <param name="splitRatio">The new owner fraction; must be in [0, 1].</param>
    /// <param name="personId">
    ///   Required when <paramref name="splitRatio"/> is less than 1;
    ///   must be <see langword="null"/> when <paramref name="splitRatio"/> is exactly 1.
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <paramref name="name"/> is null, empty, or whitespace;
    ///   or the split/person combination is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="defaultAmount"/> is negative;
    ///   or <paramref name="splitRatio"/> is outside [0, 1].
    /// </exception>
    public void Update(
        string name,
        long categoryId,
        BillKind kind,
        decimal defaultAmount,
        decimal splitRatio,
        long? personId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultAmount);

        if (splitRatio < 0m || splitRatio > 1m)
            throw new ArgumentOutOfRangeException(nameof(splitRatio), "SplitRatio must be between 0 and 1.");

        ValidateSplitPersonRule(splitRatio, personId);

        Name = name.Trim();
        CategoryId = categoryId;
        Kind = kind;
        DefaultAmount = defaultAmount;
        SplitRatio = splitRatio;
        PersonId = personId;
    }

    /// <summary>
    /// Soft-deletes this bill template by setting <see cref="Active"/> to <see langword="false"/>.
    /// </summary>
    public void Deactivate() => Active = false;

    // Enforces the split/person business rule shared by Create and Update.
    private static void ValidateSplitPersonRule(decimal splitRatio, long? personId)
    {
        if (splitRatio < 1m && personId is null)
            throw new ArgumentException("PersonId is required when SplitRatio is less than 1.", nameof(personId));

        if (splitRatio == 1m && personId is not null)
            throw new ArgumentException("PersonId must be null when SplitRatio is 1.", nameof(personId));
    }
}
