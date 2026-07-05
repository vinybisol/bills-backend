namespace Domain.Entities;

/// <summary>
/// Represents an authenticated application user, the internal identity that the
/// rest of the domain references through <see cref="Id"/>.
/// </summary>
/// <remarks>
/// The Firebase identifier (<see cref="FirebaseUid"/>) is intentionally confined to
/// this entity and must never leak into the domain. Domain owner references always
/// point at <see cref="Id"/> (an internal <see cref="long"/>), not at the Firebase uid.
/// State is encapsulated: instances are created through <see cref="Provision"/> and
/// mutated through behavior methods, never by external property assignment.
/// </remarks>
public sealed class AppUser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppUser"/> class for EF Core materialization.
    /// </summary>
    private AppUser()
    {
    }

    private AppUser(string firebaseUid, string? email, string name, DateTimeOffset createdAt)
    {
        FirebaseUid = firebaseUid;
        Email = email;
        Name = name;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Gets the internal identifier of the user.
    /// </summary>
    /// <value>The database-generated surrogate key used as the owner reference across the domain.</value>
    public long Id { get; private set; }

    /// <summary>
    /// Gets the display name of the user, as provided by the identity token.
    /// </summary>
    /// <value>
    /// The user's display name; <see cref="string.Empty"/> when the token carries no name claim.
    /// Never <see langword="null"/>.
    /// </value>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets the e-mail address associated with the user, when the identity provider supplies one.
    /// </summary>
    /// <value>The user's e-mail address, or <see langword="null"/> when the token carries no e-mail claim.</value>
    public string? Email { get; private set; }

    /// <summary>
    /// Gets the Firebase user identifier that uniquely maps an external identity to this user.
    /// </summary>
    /// <value>The Firebase <c>sub</c>/<c>user_id</c> claim value; unique across all users.</value>
    public string FirebaseUid { get; private set; } = null!;

    /// <summary>
    /// Gets the instant at which the user was provisioned.
    /// </summary>
    /// <value>The creation timestamp, stored in UTC.</value>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Provisions a new user for the given external Firebase identity.
    /// </summary>
    /// <param name="firebaseUid">The Firebase user identifier; must be non-empty.</param>
    /// <param name="email">The e-mail address from the token, or <see langword="null"/> when absent.</param>
    /// <param name="name">The display name from the token, or <see langword="null"/> when absent; stored as <see cref="string.Empty"/> when blank.</param>
    /// <param name="createdAt">The instant the user is provisioned, in UTC.</param>
    /// <returns>A new <see cref="AppUser"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="firebaseUid"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public static AppUser Provision(string firebaseUid, string? email, string? name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);
        return new AppUser(firebaseUid, email, string.IsNullOrWhiteSpace(name) ? string.Empty : name, createdAt);
    }

    /// <summary>
    /// Updates the user's e-mail to the latest value seen on the token.
    /// </summary>
    /// <param name="email">The e-mail address, or <see langword="null"/> to clear it.</param>
    public void UpdateEmail(string? email) => Email = email;

    /// <summary>
    /// Updates the user's display name to the latest value seen on the token.
    /// </summary>
    /// <param name="name">The display name, or <see langword="null"/> to reset it to <see cref="string.Empty"/>.</param>
    public void UpdateName(string? name) => Name = name ?? string.Empty;
}
