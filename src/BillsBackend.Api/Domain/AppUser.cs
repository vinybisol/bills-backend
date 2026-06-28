namespace BillsBackend.Api.Domain;

/// <summary>
/// Represents an authenticated application user, the internal identity that the
/// rest of the domain references through <see cref="Id"/>.
/// </summary>
/// <remarks>
/// The Firebase identifier (<see cref="FirebaseUid"/>) is intentionally confined to
/// this entity and must never leak into the domain. Domain owner references always
/// point at <see cref="Id"/> (an internal <see cref="long"/>), not at the Firebase uid.
/// </remarks>
public class AppUser
{
    /// <summary>
    /// Gets or sets the internal identifier of the user.
    /// </summary>
    /// <value>The database-generated surrogate key used as the owner reference across the domain.</value>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the e-mail address associated with the user, when the identity provider supplies one.
    /// </summary>
    /// <value>The user's e-mail address, or <see langword="null"/> when the token carries no e-mail claim.</value>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the Firebase user identifier that uniquely maps an external identity to this user.
    /// </summary>
    /// <value>The Firebase <c>sub</c>/<c>user_id</c> claim value; unique across all users.</value>
    public required string FirebaseUid { get; set; }

    /// <summary>
    /// Gets or sets the instant at which the user was provisioned.
    /// </summary>
    /// <value>The creation timestamp, stored in UTC.</value>
    public DateTimeOffset CreatedAt { get; set; }
}
