using System.Security.Claims;
using Api.Identity;

namespace Api.UnitTests.Identity;

[TestFixture]
public sealed class FirebaseClaimsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    // --- GetFirebaseUid ---

    [Test]
    public void GetFirebaseUid_UserIdAndOtherIdentifiers_PrefersUserId()
    {
        // Arrange
        var principal = PrincipalWith(
            new Claim("sub", "from-sub"),
            new Claim(ClaimTypes.NameIdentifier, "from-nameidentifier"),
            new Claim("user_id", "from-user_id"));

        // Act
        var uid = principal.GetFirebaseUid();

        // Assert
        Assert.That(uid, Is.EqualTo("from-user_id"));
    }

    [Test]
    public void GetFirebaseUid_NoUserId_FallsBackToNameIdentifierBeforeSub()
    {
        // Arrange
        var principal = PrincipalWith(
            new Claim("sub", "from-sub"),
            new Claim(ClaimTypes.NameIdentifier, "from-nameidentifier"));

        // Act
        var uid = principal.GetFirebaseUid();

        // Assert
        Assert.That(uid, Is.EqualTo("from-nameidentifier"));
    }

    [Test]
    public void GetFirebaseUid_OnlySub_ReturnsSub()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("sub", "from-sub"));

        // Act
        var uid = principal.GetFirebaseUid();

        // Assert
        Assert.That(uid, Is.EqualTo("from-sub"));
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

    // --- GetEmail ---

    [Test]
    public void GetEmail_EmailClaim_ReturnsIt()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("email", "carol@example.com"));

        // Act
        var email = principal.GetEmail();

        // Assert
        Assert.That(email, Is.EqualTo("carol@example.com"));
    }

    [Test]
    public void GetEmail_OnlyClaimTypesEmail_FallsBackToIt()
    {
        // Arrange
        var principal = PrincipalWith(new Claim(ClaimTypes.Email, "mapped@example.com"));

        // Act
        var email = principal.GetEmail();

        // Assert
        Assert.That(email, Is.EqualTo("mapped@example.com"));
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

    // --- GetName ---

    [Test]
    public void GetName_NameClaim_ReturnsIt()
    {
        // Arrange
        var principal = PrincipalWith(new Claim("name", "Alice Example"));

        // Act
        var name = principal.GetName();

        // Assert
        Assert.That(name, Is.EqualTo("Alice Example"));
    }

    [Test]
    public void GetName_OnlyClaimTypesName_FallsBackToIt()
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

    // --- Guards ---

    [Test]
    public void Extensions_NullPrincipal_ThrowArgumentNullException()
    {
        // Arrange
        ClaimsPrincipal principal = null!;

        // Act / Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => principal.GetFirebaseUid(), Throws.ArgumentNullException);
            Assert.That(() => principal.GetEmail(), Throws.ArgumentNullException);
            Assert.That(() => principal.GetName(), Throws.ArgumentNullException);
        }
    }
}
