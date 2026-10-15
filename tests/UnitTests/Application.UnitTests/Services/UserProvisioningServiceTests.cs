using Application.Abstractions.Services;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class UserProvisioningServiceTests
{
    private const long GeneratedUserId = 42L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private IAppUserService _users = null!;
    private ICategoryService _categories = null!;
    private UserProvisioningService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<IAppUserService>();
        _categories = Substitute.For<ICategoryService>();
        _sut = new UserProvisioningService(
            _users, _categories, new FixedTimeProvider(FixedNow), NullLogger<UserProvisioningService>.Instance);
    }

    /// <summary>No user exists yet; AddAsync "persists" the user by assigning a database id.</summary>
    private void ArrangeNewUserIsCreated()
    {
        _users.FindByFirebaseUidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);
        _users.AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(call => new UserProvisioningResult(EntityId.With(call.Arg<AppUser>(), GeneratedUserId), WasCreated: true));
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task GetOrCreateAsync_BlankFirebaseUid_ThrowsArgumentExceptionWithoutLookup(string? firebaseUid)
    {
        // Act / Assert
        await Assert.ThatAsync(
            () => _sut.GetOrCreateAsync(firebaseUid!, "x@example.com", name: null, CancellationToken.None),
            Throws.InstanceOf<ArgumentException>());
        await _users.DidNotReceiveWithAnyArgs().FindByFirebaseUidAsync(default!, default);
    }

    [Test]
    public async Task GetOrCreateAsync_ExistingUser_ReturnsItWithoutCreatingOrSeeding()
    {
        // Arrange
        var existing = AppUser.Provision("uid-1", "a@example.com", "Alice", FixedNow);
        _users.FindByFirebaseUidAsync("uid-1", Arg.Any<CancellationToken>()).Returns(existing);

        // Act
        var user = await _sut.GetOrCreateAsync("uid-1", "a@example.com", "Alice", CancellationToken.None);

        // Assert
        Assert.That(user, Is.SameAs(existing));
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _categories.DidNotReceiveWithAnyArgs().AddRangeAsync(default!, default);
    }

    [Test]
    public async Task GetOrCreateAsync_NewUser_ProvisionsUserFromTokenClaims()
    {
        // Arrange
        ArrangeNewUserIsCreated();

        // Act
        var user = await _sut.GetOrCreateAsync("uid-new", "alice@example.com", "Alice Example", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.Id, Is.EqualTo(GeneratedUserId));
            Assert.That(user.FirebaseUid, Is.EqualTo("uid-new"));
            Assert.That(user.Email, Is.EqualTo("alice@example.com"));
            Assert.That(user.Name, Is.EqualTo("Alice Example"));
            Assert.That(user.CreatedAt, Is.EqualTo(FixedNow));
        }
        await _users.Received(1).AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task GetOrCreateAsync_NewUserWithBlankName_StoresEmptyName(string? name)
    {
        // Arrange
        ArrangeNewUserIsCreated();

        // Act
        var user = await _sut.GetOrCreateAsync("uid-new", "dave@example.com", name, CancellationToken.None);

        // Assert
        Assert.That(user.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task GetOrCreateAsync_NewUser_SeedsDefaultCategoriesForNewOwner()
    {
        // Arrange
        ArrangeNewUserIsCreated();
        List<Category>? seeded = null;
        await _categories.AddRangeAsync(Arg.Do<IEnumerable<Category>>(c => seeded = [.. c]), Arg.Any<CancellationToken>());

        // Act
        await _sut.GetOrCreateAsync("uid-seed", "seed@example.com", "Seed", CancellationToken.None);

        // Assert
        Assert.That(seeded, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeded!.Select(c => c.Name), Is.EquivalentTo(Category.DefaultNames));
            Assert.That(seeded, Has.All.Property(nameof(Category.OwnerId)).EqualTo(GeneratedUserId));
            Assert.That(seeded, Has.All.Property(nameof(Category.CreatedAt)).EqualTo(FixedNow));
            Assert.That(seeded, Has.All.Property(nameof(Category.Active)).True);
        }
    }

    [Test]
    public async Task GetOrCreateAsync_LostProvisioningRace_ReturnsPersistedUserWithoutSeeding()
    {
        // Arrange — another request inserted the same uid between the lookup and the insert
        var winner = EntityId.With(AppUser.Provision("uid-race", "w@example.com", "Winner", FixedNow), 99L);
        _users.FindByFirebaseUidAsync("uid-race", Arg.Any<CancellationToken>()).Returns((AppUser?)null);
        _users.AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(new UserProvisioningResult(winner, WasCreated: false));

        // Act
        var user = await _sut.GetOrCreateAsync("uid-race", "w@example.com", "Winner", CancellationToken.None);

        // Assert
        Assert.That(user, Is.SameAs(winner));
        await _categories.DidNotReceiveWithAnyArgs().AddRangeAsync(default!, default);
    }

    [Test]
    public async Task GetOrCreateAsync_CancellationToken_IsForwardedToCollaborators()
    {
        // Arrange
        ArrangeNewUserIsCreated();
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.GetOrCreateAsync("uid-ct", null, null, cts.Token);

        // Assert
        await _users.Received(1).FindByFirebaseUidAsync("uid-ct", cts.Token);
        await _users.Received(1).AddAsync(Arg.Any<AppUser>(), cts.Token);
        await _categories.Received(1).AddRangeAsync(Arg.Any<IEnumerable<Category>>(), cts.Token);
    }
}
