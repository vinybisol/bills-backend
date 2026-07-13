using System.Security.Claims;
using Api.Identity;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="FirebaseClaims"/>, verifying how the Firebase uid and e-mail
/// are extracted from a principal.
/// </summary>
[TestFixture]
public sealed class FirebaseClaimsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Test]
    public void GetFirebaseUid_PrefersUserIdClaim()
    {
        // Arrange
        var principal = PrincipalWith(
            new Claim("user_id", "uid-from-user_id"),
            new Claim("sub", "uid-from-sub"));

        // Act
        var uid = principal.GetFirebaseUid();

        // Assert
        Assert.That(uid, Is.EqualTo("uid-from-user_id"));
    }

    [Test]
    public void GetFirebaseUid_FallsBackToNameIdentifier()
    {
        // Arrange
        var principal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, "uid-from-nameidentifier"));

        // Act
        var uid = principal.GetFirebaseUid();

        // Assert
        Assert.That(uid, Is.EqualTo("uid-from-nameidentifier"));
    }

    [Test]
    public void GetFirebaseUid_NoIdentifierClaim_ReturnsNull()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("email", "noid@example.com"));

        // Act
        var uid = principal.GetFirebaseUid();

        // Assert
        Assert.That(uid, Is.Null);
    }

    [Test]
    public void GetEmail_ReadsEmailClaim()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("email", "carol@example.com"));

        // Act
        var email = principal.GetEmail();

        // Assert
        Assert.That(email, Is.EqualTo("carol@example.com"));
    }

    [Test]
    public void GetEmail_NoEmailClaim_ReturnsNull()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("user_id", "uid-only"));

        // Act
        var email = principal.GetEmail();

        // Assert
        Assert.That(email, Is.Null);
    }

    [Test]
    public void GetName_ReadsNameClaim()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("name", "Alice Example"));

        // Act
        var name = principal.GetName();

        // Assert
        Assert.That(name, Is.EqualTo("Alice Example"));
    }

    [Test]
    public void GetName_FallsBackToClaimsTypeName()
    {
        // Arrange
        var principal = PrincipalWith(new Claim(ClaimTypes.Name, "Bob Example"));

        // Act
        var name = principal.GetName();

        // Assert
        Assert.That(name, Is.EqualTo("Bob Example"));
    }

    [Test]
    public void GetName_NoNameClaim_ReturnsNull()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("user_id", "uid-only"));

        // Act
        var name = principal.GetName();

        // Assert
        Assert.That(name, Is.Null);
    }
}
