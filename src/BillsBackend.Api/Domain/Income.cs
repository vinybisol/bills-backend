namespace BillsBackend.Api.Domain;

/// <summary>
/// Represents an income template (molde de receita) in the owner's financial plan.
/// </summary>
/// <remarks>
/// Incomes are soft-deleted via <see cref="Deactivate"/> — <see cref="Active"/> is set to
/// <see langword="false"/> and the row is never physically removed.
/// Names are <em>not</em> unique per owner: the same income name may appear multiple times.
/// State is encapsulated: create through <see cref="Create"/> and mutate through behavior
/// methods only.
/// </remarks>
public sealed class Income
{
    private Income() { }

    private Income(long ownerId, string name, IncomeKind kind, decimal defaultAmount, DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        Name = name;
        Kind = kind;
        DefaultAmount = defaultAmount;
        Active = true;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the database-generated identifier.</summary>
    public long Id { get; private set; }

    /// <summary>Gets the internal <c>app_user.id</c> of the user who owns this income template.</summary>
    public long OwnerId { get; private set; }

    /// <summary>Gets the display name of the income template.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Gets the income kind: recurring or one-off.</summary>
    public IncomeKind Kind { get; private set; }

    /// <summary>Gets the default planned amount for this income template. Zero or greater.</summary>
    public decimal DefaultAmount { get; private set; }

    /// <summary>
    /// Gets whether the income template is active.
    /// <see langword="false"/> means it has been soft-deleted and must not appear in listings.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>Gets the UTC instant at which the income template was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a new active income template for the given owner.
    /// </summary>
    /// <param name="ownerId">The internal user id; must be positive.</param>
    /// <param name="name">The income name; trimmed, must not be blank.</param>
    /// <param name="kind">The income kind.</param>
    /// <param name="defaultAmount">The default planned amount; must be zero or greater.</param>
    /// <param name="createdAt">The UTC creation timestamp.</param>
    /// <returns>A new active <see cref="Income"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="ownerId"/> is not positive;
    ///   or <paramref name="defaultAmount"/> is negative.
    /// </exception>
    public static Income Create(long ownerId, string name, IncomeKind kind, decimal defaultAmount, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultAmount);
        return new Income(ownerId, name.Trim(), kind, defaultAmount, createdAt);
    }

    /// <summary>
    /// Updates all editable fields of the income template.
    /// </summary>
    /// <param name="name">The new name; trimmed, must not be blank.</param>
    /// <param name="kind">The new kind.</param>
    /// <param name="defaultAmount">The new default amount; must be zero or greater.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultAmount"/> is negative.</exception>
    public void Update(string name, IncomeKind kind, decimal defaultAmount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultAmount);
        Name = name.Trim();
        Kind = kind;
        DefaultAmount = defaultAmount;
    }

    /// <summary>
    /// Soft-deletes this income template by setting <see cref="Active"/> to <see langword="false"/>.
    /// </summary>
    public void Deactivate() => Active = false;
}
