using Domain.Entities;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="Category"/> domain rules.
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
    public void Create_BlankName_ThrowsArgumentException(string? name) => Assert.That(
            () => Category.Create(1L, name!, FixedNow),
            Throws.InstanceOf<ArgumentException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) => Assert.That(
            () => Category.Create(ownerId, "Moradia", FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

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
    public void DefaultNames_ContainsSevenEntries() => Assert.That(Category.DefaultNames, Has.Count.EqualTo(7));

    [Test]
    public void DefaultNames_ContainsExpectedCategories() => Assert.That(Category.DefaultNames, Is.EquivalentTo(new[]
        {
            "Moradia", "Saúde", "Transporte", "Alimentação", "Lazer", "Educação", "Outros"
        }));

}
