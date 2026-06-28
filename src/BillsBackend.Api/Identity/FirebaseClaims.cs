using System.Security.Claims;

namespace BillsBackend.Api.Identity;

/// <summary>
/// Helpers for reading Firebase identity information out of a <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// Firebase places the user id in the <c>sub</c> (and <c>user_id</c>) claim and, when
/// available, the e-mail in the <c>email</c> claim. The JWT handler maps <c>sub</c> to
/// <see cref="ClaimTypes.NameIdentifier"/>, so both names are checked.
/// </remarks>
public static class FirebaseClaims
{
    /// <summary>
    /// Extracts the Firebase user identifier from the supplied principal.
    /// </summary>
    /// <param name="principal">The authenticated principal to read.</param>
    /// <returns>The Firebase uid, or <see langword="null"/> when no identifier claim is present.</returns>
    public static string? GetFirebaseUid(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue("user_id")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
    }

    /// <summary>
    /// Extracts the e-mail address from the supplied principal, when present.
    /// </summary>
    /// <param name="principal">The authenticated principal to read.</param>
    /// <returns>The e-mail address, or <see langword="null"/> when no e-mail claim is present.</returns>
    public static string? GetEmail(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue("email")
            ?? principal.FindFirstValue(ClaimTypes.Email);
    }
}
