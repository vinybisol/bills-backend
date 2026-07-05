namespace Domain.Abstractions.Filters;

/// <summary>
/// Provides the internal id of the authenticated owner for the current request,
/// used to scope all domain queries to a single user.
/// </summary>
public interface ICurrentOwner
{
    /// <summary>Gets or sets the internal <c>app_user.id</c> of the authenticated user.</summary>
    long Id { get; }
    void SetCurrentOwnerId(long id);
}