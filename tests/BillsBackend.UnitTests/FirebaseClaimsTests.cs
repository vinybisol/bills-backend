using System.Security.Claims;
using BillsBackend.Api.Identity;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="FirebaseClaims"/>, verifying how the Firebase uid and e-mail
/// are extracted from a principal.
/// </summary>
[TestFixture]
public class FirebaseClaimsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Test]
    public void GetFirebaseUid_PrefersUserIdClaim()
    {
        var principal = PrincipalWith(
            new Claim("user_id", "uid-from-user_id"),
            new Claim("sub", "uid-from-sub"));

        Assert.That(principal.GetFirebaseUid(), Is.EqualTo("uid-from-user_id"));
    }

    [Test]
    public void GetFirebaseUid_FallsBackToNameIdentifier()
    {
        var principal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, "uid-from-nameidentifier"));

        Assert.That(principal.GetFirebaseUid(), Is.EqualTo("uid-from-nameidentifier"));
    }

    [Test]
    public void GetFirebaseUid_NoIdentifierClaim_ReturnsNull()
    {
        var principal = PrincipalWith(new Claim("email", "noid@example.com"));

        Assert.That(principal.GetFirebaseUid(), Is.Null);
    }

    [Test]
    public void GetEmail_ReadsEmailClaim()
    {
        var principal = PrincipalWith(new Claim("email", "carol@example.com"));

        Assert.That(principal.GetEmail(), Is.EqualTo("carol@example.com"));
    }

    [Test]
    public void GetEmail_NoEmailClaim_ReturnsNull()
    {
        var principal = PrincipalWith(new Claim("user_id", "uid-only"));

        Assert.That(principal.GetEmail(), Is.Null);
    }
}
