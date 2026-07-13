namespace Domain.Entities;

/// <summary>
/// Represents a budget category owned by one user, used to classify bill templates.
/// </summary>
/// <remarks>
/// Categories are soft-deleted via <see cref="Deactivate"/> — <see cref="Active"/> is set to
/// <see langword="false"/> and the row is never physically removed.
/// Names must be unique per owner within active categories; the uniqueness constraint is
/// enforced both at the application layer and by a database unique index on
/// <c>(owner_id, name)</c>.
/// State is encapsulated: create through <see cref="Create"/> and mutate through behavior
/// methods only.
/// </remarks>
public sealed class Category
{
    private Category() { }

    private Category(long ownerId, string name, DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        Name = name;
        Active = true;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the database-generated identifier.</summary>
    public long Id { get; private set; }

    /// <summary>Gets the internal <c>app_user.id</c> of the user who owns this category.</summary>
    public long OwnerId { get; private set; }

    /// <summary>Gets the display name of the category.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets whether the category is active.
    /// <see langword="false"/> means it has been soft-deleted and must not appear in listings.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>Gets the UTC instant at which the category was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The default category names seeded automatically for every newly provisioned user.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultNames =
        ["Moradia", "Saúde", "Transporte", "Alimentação", "Lazer", "Educação", "Outros"];

    /// <summary>
    /// Creates a new active category for the given owner.
    /// </summary>
    /// <param name="ownerId">The internal user id; must be positive.</param>
    /// <param name="name">The category name; trimmed, must not be blank.</param>
    /// <param name="createdAt">The UTC creation timestamp.</param>
    /// <returns>A new active <see cref="Category"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownerId"/> is not positive.</exception>
    public static Category Create(long ownerId, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        return new Category(ownerId, name.Trim(), createdAt);
    }

    /// <summary>
    /// Renames the category.
    /// </summary>
    /// <param name="newName">The new name; trimmed, must not be blank.</param>
    /// <exception cref="ArgumentException"><paramref name="newName"/> is null, empty, or whitespace.</exception>
    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
    }

    /// <summary>
    /// Soft-deletes this category by setting <see cref="Active"/> to <see langword="false"/>.
    /// </summary>
    public void Deactivate() => Active = false;
}
