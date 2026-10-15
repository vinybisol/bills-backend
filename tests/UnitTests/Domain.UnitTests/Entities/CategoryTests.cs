using Domain.Entities;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class CategoryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Create_BlankName_ThrowsArgumentException(string? name) =>
        Assert.That(() => Category.Create(1L, name!, FixedNow), Throws.InstanceOf<ArgumentException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(() => Category.Create(ownerId, "Moradia", FixedNow), Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_ValidArgs_ReturnsActiveCategory()
    {
        // Act
        var category = Category.Create(1L, "Moradia", FixedNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(category.OwnerId, Is.EqualTo(1L));
            Assert.That(category.Name, Is.EqualTo("Moradia"));
            Assert.That(category.Active, Is.True);
            Assert.That(category.CreatedAt, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        // Act
        var category = Category.Create(1L, "  Lazer  ", FixedNow);

        // Assert
        Assert.That(category.Name, Is.EqualTo("Lazer"));
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Rename_BlankName_ThrowsArgumentExceptionAndKeepsName(string? name)
    {
        // Arrange
        var category = Category.Create(1L, "Moradia", FixedNow);

        // Act
        Assert.That(() => category.Rename(name!), Throws.InstanceOf<ArgumentException>());

        // Assert
        Assert.That(category.Name, Is.EqualTo("Moradia"));
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

    [Test]
    public void Deactivate_ActiveCategory_SetsActiveFalse()
    {
        // Arrange
        var category = Category.Create(1L, "Lazer", FixedNow);

        // Act
        category.Deactivate();

        // Assert
        Assert.That(category.Active, Is.False);
    }

    [Test]
    public void DefaultNames_Always_ContainsTheSevenSeedCategories() =>
        Assert.That(
            Category.DefaultNames,
            Is.EquivalentTo(new[] { "Moradia", "Saúde", "Transporte", "Alimentação", "Lazer", "Educação", "Outros" }));
}
