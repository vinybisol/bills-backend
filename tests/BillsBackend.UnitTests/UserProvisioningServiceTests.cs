using BillsBackend.Api.Data;
using BillsBackend.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="UserProvisioningService"/>, covering the translation of a
/// Firebase uid into the internal <c>app_user</c> and just-in-time provisioning.
/// </summary>
[TestFixture]
public sealed class UserProvisioningServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 28, 12, 00, 00, TimeSpan.Zero);

    private static UserProvisioningService CreateService(AppDbContext db) =>
        new(db, new FixedTimeProvider(FixedNow), NullLogger<UserProvisioningService>.Instance);

    [Test]
    public async Task GetOrCreateAsync_NewFirebaseUid_CreatesAndPersistsUser()
    {
        // Arrange
        await using var db = TestSupport.NewInMemoryContext();
        var service = CreateService(db);

        // Act
        var user = await service.GetOrCreateAsync("firebase-uid-1", "alice@example.com", "Alice Example");

        // Assert
        Assert.That(user.Id, Is.GreaterThan(0));
        Assert.That(user.FirebaseUid, Is.EqualTo("firebase-uid-1"));
        Assert.That(user.Email, Is.EqualTo("alice@example.com"));

        var persisted = await db.Users.SingleAsync();
        Assert.That(persisted.Id, Is.EqualTo(user.Id));
    }

    [Test]
    public async Task GetOrCreateAsync_NewUser_StampsCreatedAtFromTimeProvider()
    {
        // Arrange
        await using var db = TestSupport.NewInMemoryContext();
        var service = CreateService(db);

        // Act
        var user = await service.GetOrCreateAsync("firebase-uid-2", email: null, name: null);

        // Assert
        Assert.That(user.CreatedAt, Is.EqualTo(FixedNow));
    }

    [Test]
    public async Task GetOrCreateAsync_ExistingFirebaseUid_ReturnsSameUserWithoutDuplicate()
    {
        // Arrange
        await using var db = TestSupport.NewInMemoryContext();
        var service = CreateService(db);

        // Act
        var first = await service.GetOrCreateAsync("firebase-uid-3", "bob@example.com", "Bob Example");
        var second = await service.GetOrCreateAsync("firebase-uid-3", "bob@example.com", "Bob Example");

        // Assert
        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(await db.Users.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task GetOrCreateAsync_NewUser_PersistsProvidedName()
    {
        // Arrange
        await using var db = TestSupport.NewInMemoryContext();
        var service = CreateService(db);

        // Act
        var user = await service.GetOrCreateAsync("firebase-uid-4", "carol@example.com", "Carol Example");

        // Assert
        Assert.That(user.Name, Is.EqualTo("Carol Example"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetOrCreateAsync_NewUserWithBlankOrNullName_PersistsEmptyString(string? name)
    {
        // Arrange
        await using var db = TestSupport.NewInMemoryContext();
        var service = CreateService(db);

        // Act
        var user = await service.GetOrCreateAsync("firebase-uid-5", "dave@example.com", name);

        // Assert
        Assert.That(user.Name, Is.EqualTo(string.Empty));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetOrCreateAsync_MissingFirebaseUid_ThrowsArgumentException(string? firebaseUid)
    {
        // Arrange
        using var db = TestSupport.NewInMemoryContext();
        var service = CreateService(db);

        // Act / Assert
        Assert.That(
            async () => await service.GetOrCreateAsync(firebaseUid!, "x@example.com", name: null),
            Throws.InstanceOf<ArgumentException>());
    }
}
