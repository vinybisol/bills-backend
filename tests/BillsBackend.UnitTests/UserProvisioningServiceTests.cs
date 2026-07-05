using System.Reflection;
using Application.Abstractions.Repositories;
using Application.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="UserProvisioningService"/>, covering the translation of a
/// Firebase uid into the internal <c>app_user</c>, just-in-time provisioning, and the
/// default-category seeding that runs the first time a user is provisioned.
/// </summary>
[TestFixture]
public sealed class UserProvisioningServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 28, 12, 00, 00, TimeSpan.Zero);

    private IAppUserRepository _users = null!;
    private ICategoryRepository _categories = null!;

    /// <summary>Creates fresh mocks before each test — NUnit reuses a single fixture instance
    /// across all test methods, so instance-level substitutes must be re-created here rather
    /// than via field initializers to avoid leaking call history between tests.</summary>
    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<IAppUserRepository>();
        _categories = Substitute.For<ICategoryRepository>();
    }

    private UserProvisioningService CreateService() =>
        new(_users, _categories, new FixedTimeProvider(FixedNow), NullLogger<UserProvisioningService>.Instance);

    /// <summary>Configures <see cref="_users"/> as if no user exists yet, and <see cref="IAppUserRepository.AddAsync"/>
    /// always succeeds by persisting whatever <see cref="AppUser"/> it is given. Assigns a fake
    /// database-generated id, mirroring what EF Core does to <see cref="AppUser.Id"/> on
    /// <c>SaveChangesAsync</c> in the real <see cref="IAppUserRepository"/> implementation —
    /// required because <see cref="Category.Create"/> demands a positive owner id.</summary>
    private void SetUpNoExistingUser()
    {
        _users.FindByFirebaseUidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AppUser?)null);
        _users.AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var user = (AppUser)callInfo[0];
                AssignGeneratedId(user, 42L);
                return new UserProvisioningResult(user, true);
            });
    }

    /// <summary>Sets the private, EF-managed <see cref="AppUser.Id"/> via reflection, simulating
    /// the id EF Core assigns after a real insert.</summary>
    private static void AssignGeneratedId(AppUser user, long id) =>
        typeof(AppUser).GetProperty(nameof(AppUser.Id))!.SetValue(user, id);

    [Test]
    public async Task GetOrCreateAsync_NewFirebaseUid_CreatesAndPersistsUser()
    {
        // Arrange
        SetUpNoExistingUser();
        var service = CreateService();

        // Act
        var user = await service.GetOrCreateAsync("firebase-uid-1", "alice@example.com", "Alice Example");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.FirebaseUid, Is.EqualTo("firebase-uid-1"));
            Assert.That(user.Email, Is.EqualTo("alice@example.com"));
        }
        await _users.Received(1).AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetOrCreateAsync_NewUser_StampsCreatedAtFromTimeProvider()
    {
        // Arrange
        SetUpNoExistingUser();
        var service = CreateService();

        // Act
        var user = await service.GetOrCreateAsync("firebase-uid-2", email: null, name: null);

        // Assert
        Assert.That(user.CreatedAt, Is.EqualTo(FixedNow));
    }

    [Test]
    public async Task GetOrCreateAsync_ExistingFirebaseUid_ReturnsSameUserWithoutDuplicate()
    {
        // Arrange
        AppUser? created = null;
        _users.FindByFirebaseUidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => created);
        _users.AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                created = (AppUser)callInfo[0];
                return new UserProvisioningResult(created, true);
            });
        var service = CreateService();

        // Act
        var first = await service.GetOrCreateAsync("firebase-uid-3", "bob@example.com", "Bob Example");
        var second = await service.GetOrCreateAsync("firebase-uid-3", "bob@example.com", "Bob Example");

        // Assert
        Assert.That(second, Is.SameAs(first));
        await _users.Received(1).AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetOrCreateAsync_NewUser_PersistsProvidedName()
    {
        // Arrange
        SetUpNoExistingUser();
        var service = CreateService();

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
        SetUpNoExistingUser();
        var service = CreateService();

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
        var service = CreateService();

        // Act / Assert
        Assert.That(
            async () => await service.GetOrCreateAsync(firebaseUid!, "x@example.com", name: null),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task GetOrCreateAsync_NewUser_SeedsSevenDefaultCategoriesForNewOwner()
    {
        // Arrange
        SetUpNoExistingUser();
        var service = CreateService();

        // Act
        var persisted = await service.GetOrCreateAsync("firebase-seed-1", "seed@example.com", "Seed User");

        // Assert
        await _categories.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<Category>>(cats =>
                cats.Count() == 7
                && cats.All(c => c.OwnerId == persisted.Id)
                && cats.Select(c => c.Name).OrderBy(n => n)
                    .SequenceEqual(Category.DefaultNames.OrderBy(n => n))),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetOrCreateAsync_ExistingUser_DoesNotReseedCategories()
    {
        // Arrange
        var existing = AppUser.Provision("firebase-seed-2", "seed2@example.com", "Seed 2", FixedNow);
        _users.FindByFirebaseUidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);
        var service = CreateService();

        // Act
        await service.GetOrCreateAsync("firebase-seed-2", "seed2@example.com", "Seed 2");

        // Assert
        await _categories.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<Category>>(), Arg.Any<CancellationToken>());
    }
}
