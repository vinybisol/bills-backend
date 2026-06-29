using BillsBackend.Api.Domain;
using BillsBackend.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="Category"/> domain rules and for the default-category seeding
/// performed by <see cref="UserProvisioningService"/> on first user provisioning.
/// </summary>
[TestFixture]
public sealed class CategoryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 29, 10, 00, 00, TimeSpan.Zero);

    // --- Category.Create guards ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_BlankName_ThrowsArgumentException(string? name)
    {
        Assert.That(
            () => Category.Create(1L, name!, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId)
    {
        Assert.That(
            () => Category.Create(ownerId, "Moradia", FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_ValidArgs_ReturnsActiveCategory()
    {
        // Act
        var category = Category.Create(1L, "Moradia", FixedNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(category.OwnerId, Is.EqualTo(1L));
            Assert.That(category.Name, Is.EqualTo("Moradia"));
            Assert.That(category.Active, Is.True);
            Assert.That(category.CreatedAt, Is.EqualTo(FixedNow));
        });
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        // Act
        var category = Category.Create(1L, "  Lazer  ", FixedNow);

        // Assert
        Assert.That(category.Name, Is.EqualTo("Lazer"));
    }

    // --- Category.Rename ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Rename_BlankName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var category = Category.Create(1L, "Moradia", FixedNow);

        // Act / Assert
        Assert.That(
            () => category.Rename(name!),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Rename_ValidName_UpdatesAndTrimsName()
    {
        // Arrange
        var category = Category.Create(1L, "Moradia", FixedNow);

        // Act
        category.Rename("  Habitação  ");

        // Assert
        Assert.That(category.Name, Is.EqualTo("Habitação"));
    }

    // --- Category.Deactivate ---

    [Test]
    public void Deactivate_ActiveCategory_SetsActiveToFalse()
    {
        // Arrange
        var category = Category.Create(1L, "Lazer", FixedNow);

        // Act
        category.Deactivate();

        // Assert
        Assert.That(category.Active, Is.False);
    }

    // --- DefaultNames ---

    [Test]
    public void DefaultNames_ContainsSevenEntries()
    {
        Assert.That(Category.DefaultNames, Has.Count.EqualTo(7));
    }

    [Test]
    public void DefaultNames_ContainsExpectedCategories()
    {
        Assert.That(Category.DefaultNames, Is.EquivalentTo(new[]
        {
            "Moradia", "Saúde", "Transporte", "Alimentação", "Lazer", "Educação", "Outros"
        }));
    }

    // --- Seeding via UserProvisioningService ---

    private static UserProvisioningService CreateService(BillsBackend.Api.Data.AppDbContext db) =>
        new(db, new FixedTimeProvider(FixedNow), NullLogger<UserProvisioningService>.Instance);

    [Test]
    public async Task GetOrCreateAsync_NewUser_SeedsSevenDefaultCategories()
    {
        // Arrange — keep a reference to the owner context so we can set Id after provisioning
        var owner = new TestCurrentOwner();
        await using var db = TestSupport.NewInMemoryContext(owner);
        var service = CreateService(db);

        // Act
        var user = await service.GetOrCreateAsync("firebase-seed-1", "seed@example.com", "Seed User");

        // Set owner id so the query filter resolves to this user's rows
        owner.Id = user.Id;

        // Assert
        var categories = await db.Categories.ToListAsync();
        Assert.That(categories, Has.Count.EqualTo(7));
        Assert.That(categories.Select(c => c.Name), Is.EquivalentTo(Category.DefaultNames));
    }

    [Test]
    public async Task GetOrCreateAsync_ExistingUser_DoesNotReseedCategories()
    {
        // Arrange
        var owner = new TestCurrentOwner();
        await using var db = TestSupport.NewInMemoryContext(owner);
        var service = CreateService(db);

        // Act — provision twice with the same firebase uid
        var user = await service.GetOrCreateAsync("firebase-seed-2", "seed2@example.com", "Seed 2");
        await service.GetOrCreateAsync("firebase-seed-2", "seed2@example.com", "Seed 2");

        // Assert — bypass filter to count all rows for this owner (should still be 7, not 14)
        var allForUser = await db.Categories.IgnoreQueryFilters()
            .Where(c => c.OwnerId == user.Id)
            .ToListAsync();
        Assert.That(allForUser, Has.Count.EqualTo(7));
    }

    [Test]
    public async Task GetOrCreateAsync_TwoDistinctNewUsers_EachGetSevenCategories()
    {
        // Arrange — two separate owner contexts, but one shared in-memory database
        var dbName = $"unit-seed-dual-{Guid.NewGuid()}";
        var ownerA = new TestCurrentOwner();
        var ownerB = new TestCurrentOwner();

        var opts = new DbContextOptionsBuilder<BillsBackend.Api.Data.AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbA = new BillsBackend.Api.Data.AppDbContext(opts, ownerA);
        await using var dbB = new BillsBackend.Api.Data.AppDbContext(opts, ownerB);

        var serviceA = new UserProvisioningService(dbA, new FixedTimeProvider(FixedNow), NullLogger<UserProvisioningService>.Instance);
        var serviceB = new UserProvisioningService(dbB, new FixedTimeProvider(FixedNow), NullLogger<UserProvisioningService>.Instance);

        // Act
        var userA = await serviceA.GetOrCreateAsync("firebase-dual-a", "a@example.com", "User A");
        var userB = await serviceB.GetOrCreateAsync("firebase-dual-b", "b@example.com", "User B");

        // Assert — each user sees exactly their own 7 categories, no cross-owner leak
        ownerA.Id = userA.Id;
        ownerB.Id = userB.Id;

        var catsA = await dbA.Categories.ToListAsync();
        var catsB = await dbB.Categories.ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(catsA, Has.Count.EqualTo(7));
            Assert.That(catsB, Has.Count.EqualTo(7));
            Assert.That(catsA.Select(c => c.OwnerId), Is.All.EqualTo(userA.Id));
            Assert.That(catsB.Select(c => c.OwnerId), Is.All.EqualTo(userB.Id));
        });
    }
}
