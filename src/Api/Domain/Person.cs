namespace BillsBackend.Api.Domain;

/// <summary>
/// Represents a person in the user's financial network — a contact who may share bills
/// or owe money to the owner.
/// </summary>
/// <remarks>
/// Persons are soft-deleted via <see cref="Deactivate"/> — <see cref="Active"/> is set to
/// <see langword="false"/> and the row is never physically removed.
/// Unlike categories, names are <em>not</em> unique per owner: the same person's name may
/// appear multiple times (e.g., two contacts both named "Ana").
/// The <see cref="AppUserId"/> field is nullable and reserved for Phase 2, when a person
/// will optionally be linked to an <c>app_user</c> account.
/// State is encapsulated: create through <see cref="Create"/> and mutate through behavior
/// methods only.
/// </remarks>
public sealed class Person
{
    private Person() { }

    private Person(long ownerId, string name, DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        Name = name;
        Active = true;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the database-generated identifier.</summary>
    public long Id { get; private set; }

    /// <summary>Gets the internal <c>app_user.id</c> of the user who owns this person.</summary>
    public long OwnerId { get; private set; }

    /// <summary>Gets the display name of the person.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets the optional link to an <c>app_user</c> account.
    /// <see langword="null"/> until Phase 2 wires the relationship.
    /// </summary>
    public long? AppUserId { get; private set; }

    /// <summary>
    /// Gets whether the person is active.
    /// <see langword="false"/> means it has been soft-deleted and must not appear in listings.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>Gets the UTC instant at which the person was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a new active person for the given owner.
    /// </summary>
    /// <param name="ownerId">The internal user id; must be positive.</param>
    /// <param name="name">The person's name; trimmed, must not be blank.</param>
    /// <param name="createdAt">The UTC creation timestamp.</param>
    /// <returns>A new active <see cref="Person"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownerId"/> is not positive.</exception>
    public static Person Create(long ownerId, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        return new Person(ownerId, name.Trim(), createdAt);
    }

    /// <summary>
    /// Renames the person.
    /// </summary>
    /// <param name="newName">The new name; trimmed, must not be blank.</param>
    /// <exception cref="ArgumentException"><paramref name="newName"/> is null, empty, or whitespace.</exception>
    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName.Trim();
    }

    /// <summary>
    /// Soft-deletes this person by setting <see cref="Active"/> to <see langword="false"/>.
    /// </summary>
    public void Deactivate() => Active = false;
}
