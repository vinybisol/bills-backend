using Domain.Entities;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class PersonTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Create_BlankName_ThrowsArgumentException(string? name) =>
        Assert.That(() => Person.Create(1L, name!, FixedNow), Throws.InstanceOf<ArgumentException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(() => Person.Create(ownerId, "Ana", FixedNow), Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_ValidArgs_ReturnsActivePersonWithoutAppUser()
    {
        // Act
        var person = Person.Create(1L, "Ana", FixedNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(person.OwnerId, Is.EqualTo(1L));
            Assert.That(person.Name, Is.EqualTo("Ana"));
            Assert.That(person.Active, Is.True);
            Assert.That(person.AppUserId, Is.Null);
            Assert.That(person.CreatedAt, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        // Act
        var person = Person.Create(1L, "  João  ", FixedNow);

        // Assert
        Assert.That(person.Name, Is.EqualTo("João"));
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Rename_BlankName_ThrowsArgumentExceptionAndKeepsName(string? name)
    {
        // Arrange
        var person = Person.Create(1L, "Ana", FixedNow);

        // Act
        Assert.That(() => person.Rename(name!), Throws.InstanceOf<ArgumentException>());

        // Assert
        Assert.That(person.Name, Is.EqualTo("Ana"));
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

    [Test]
    public void Deactivate_ActivePerson_SetsActiveFalse()
    {
        // Arrange
        var person = Person.Create(1L, "Ana", FixedNow);

        // Act
        person.Deactivate();

        // Assert
        Assert.That(person.Active, Is.False);
    }
}
