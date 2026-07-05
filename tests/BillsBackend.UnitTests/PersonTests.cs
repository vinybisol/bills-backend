using Domain.Entities;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="Person"/> domain rules.
/// </summary>
[TestFixture]
public sealed class PersonTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 29, 10, 00, 00, TimeSpan.Zero);

    // --- Person.Create guards ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_BlankName_ThrowsArgumentException(string? name)
    {
        Assert.That(
            () => Person.Create(1L, name!, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId)
    {
        Assert.That(
            () => Person.Create(ownerId, "Ana", FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_ValidArgs_ReturnsActivePerson()
    {
        // Act
        var person = Person.Create(1L, "Ana", FixedNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(person.OwnerId, Is.EqualTo(1L));
            Assert.That(person.Name, Is.EqualTo("Ana"));
            Assert.That(person.Active, Is.True);
            Assert.That(person.CreatedAt, Is.EqualTo(FixedNow));
        });
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        // Act
        var person = Person.Create(1L, "  João  ", FixedNow);

        // Assert
        Assert.That(person.Name, Is.EqualTo("João"));
    }

    [Test]
    public void Create_AppUserIdIsNullByDefault()
    {
        // Act
        var person = Person.Create(1L, "Ana", FixedNow);

        // Assert
        Assert.That(person.AppUserId, Is.Null);
    }

    // --- Person.Rename ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Rename_BlankName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var person = Person.Create(1L, "Ana", FixedNow);

        // Act / Assert
        Assert.That(
            () => person.Rename(name!),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Rename_ValidName_UpdatesAndTrimsName()
    {
        // Arrange
        var person = Person.Create(1L, "Ana", FixedNow);

        // Act
        person.Rename("  Maria  ");

        // Assert
        Assert.That(person.Name, Is.EqualTo("Maria"));
    }

    // --- Person.Deactivate ---

    [Test]
    public void Deactivate_ActivePerson_SetsActiveToFalse()
    {
        // Arrange
        var person = Person.Create(1L, "Ana", FixedNow);

        // Act
        person.Deactivate();

        // Assert
        Assert.That(person.Active, Is.False);
    }
}
