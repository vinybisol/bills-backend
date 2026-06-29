namespace BillsBackend.Api.Identity;

/// <summary>
/// Provides the internal id of the authenticated owner for the current request,
/// used to scope all domain queries to a single user.
/// </summary>
public interface ICurrentOwner
{
    /// <summary>Gets or sets the internal <c>app_user.id</c> of the authenticated user.</summary>
    long Id { get; set; }
}

/// <summary>Default scoped implementation of <see cref="ICurrentOwner"/>.</summary>
internal sealed class CurrentOwner : ICurrentOwner
{
    /// <inheritdoc/>
    public long Id { get; set; }
}
