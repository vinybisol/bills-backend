using Domain.Entities;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class AppUserTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Provision_BlankFirebaseUid_ThrowsArgumentException(string? firebaseUid) =>
        Assert.That(
            () => AppUser.Provision(firebaseUid!, "alice@example.com", "Alice", FixedNow),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Provision_ValidArgs_SetsAllProperties()
    {
        // Act
        var user = AppUser.Provision("uid-1", "alice@example.com", "Alice", FixedNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.Id, Is.Zero);
            Assert.That(user.FirebaseUid, Is.EqualTo("uid-1"));
            Assert.That(user.Email, Is.EqualTo("alice@example.com"));
            Assert.That(user.Name, Is.EqualTo("Alice"));
            Assert.That(user.CreatedAt, Is.EqualTo(FixedNow));
        }
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Provision_BlankName_StoresEmptyString(string? name)
    {
        // Act
        var user = AppUser.Provision("uid-1", "alice@example.com", name, FixedNow);

        // Assert
        Assert.That(user.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Provision_NullEmail_KeepsEmailNull()
    {
        // Act
        var user = AppUser.Provision("uid-1", null, "Alice", FixedNow);

        // Assert
        Assert.That(user.Email, Is.Null);
    }

    [TestCase("new@example.com")]
    [TestCase(null)]
    public void UpdateEmail_AnyValue_ReplacesEmail(string? email)
    {
        // Arrange
        var user = AppUser.Provision("uid-1", "old@example.com", "Alice", FixedNow);

        // Act
        user.UpdateEmail(email);

        // Assert
        Assert.That(user.Email, Is.EqualTo(email));
    }

    [Test]
    public void UpdateName_NewName_ReplacesName()
    {
        // Arrange
        var user = AppUser.Provision("uid-1", null, "Alice", FixedNow);

        // Act
        user.UpdateName("Alice Example");

        // Assert
        Assert.That(user.Name, Is.EqualTo("Alice Example"));
    }

    [Test]
    public void UpdateName_Null_ResetsToEmptyString()
    {
        // Arrange
        var user = AppUser.Provision("uid-1", null, "Alice", FixedNow);

        // Act
        user.UpdateName(null);

        // Assert
        Assert.That(user.Name, Is.EqualTo(string.Empty));
    }
}
