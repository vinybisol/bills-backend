using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="Income"/> domain rules.
/// </summary>
[TestFixture]
public sealed class IncomeTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 29, 10, 00, 00, TimeSpan.Zero);

    // --- Income.Create guards ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_BlankName_ThrowsArgumentException(string? name)
    {
        Assert.That(
            () => Income.Create(1L, name!, "recurring", 1000m, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase("salary")]
    [TestCase("")]
    [TestCase("monthly")]
    [TestCase("RECURRING")]
    [TestCase("one-off")]
    public void Create_InvalidKind_ThrowsArgumentException(string kind)
    {
        Assert.That(
            () => Income.Create(1L, "Salário", kind, 1000m, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException()
    {
        Assert.That(
            () => Income.Create(1L, "Salário", "recurring", -0.01m, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId)
    {
        Assert.That(
            () => Income.Create(ownerId, "Salário", "recurring", 1000m, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_ValidArgs_ReturnsActiveIncome()
    {
        // Act
        var income = Income.Create(1L, "Salário", "recurring", 5000m, FixedNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(income.OwnerId, Is.EqualTo(1L));
            Assert.That(income.Name, Is.EqualTo("Salário"));
            Assert.That(income.Kind, Is.EqualTo("recurring"));
            Assert.That(income.DefaultAmount, Is.EqualTo(5000m));
            Assert.That(income.Active, Is.True);
            Assert.That(income.CreatedAt, Is.EqualTo(FixedNow));
        });
    }

    [Test]
    public void Create_ZeroDefaultAmount_IsAllowed()
    {
        // Act
        var income = Income.Create(1L, "Bônus eventual", "one_off", 0m, FixedNow);

        // Assert
        Assert.That(income.DefaultAmount, Is.EqualTo(0m));
    }

    [TestCase("recurring")]
    [TestCase("one_off")]
    public void Create_ValidKind_SetsKind(string kind)
    {
        // Act
        var income = Income.Create(1L, "Renda", kind, 100m, FixedNow);

        // Assert
        Assert.That(income.Kind, Is.EqualTo(kind));
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        // Act
        var income = Income.Create(1L, "  Salário  ", "recurring", 5000m, FixedNow);

        // Assert
        Assert.That(income.Name, Is.EqualTo("Salário"));
    }

    // --- Income.Update ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Update_BlankName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var income = Income.Create(1L, "Salário", "recurring", 5000m, FixedNow);

        // Act / Assert
        Assert.That(
            () => income.Update(name!, "recurring", 5000m),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase("salary")]
    [TestCase("")]
    [TestCase("one-off")]
    public void Update_InvalidKind_ThrowsArgumentException(string kind)
    {
        // Arrange
        var income = Income.Create(1L, "Salário", "recurring", 5000m, FixedNow);

        // Act / Assert
        Assert.That(
            () => income.Update("Salário", kind, 5000m),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Update_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var income = Income.Create(1L, "Salário", "recurring", 5000m, FixedNow);

        // Act / Assert
        Assert.That(
            () => income.Update("Salário", "recurring", -1m),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Update_ChangesFields()
    {
        // Arrange
        var income = Income.Create(1L, "Salário", "recurring", 5000m, FixedNow);

        // Act
        income.Update("Freelance", "one_off", 2500m);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(income.Name, Is.EqualTo("Freelance"));
            Assert.That(income.Kind, Is.EqualTo("one_off"));
            Assert.That(income.DefaultAmount, Is.EqualTo(2500m));
        });
    }

    [Test]
    public void Update_NameWithSurroundingWhitespace_TrimsName()
    {
        // Arrange
        var income = Income.Create(1L, "Salário", "recurring", 5000m, FixedNow);

        // Act
        income.Update("  Freelance  ", "one_off", 2500m);

        // Assert
        Assert.That(income.Name, Is.EqualTo("Freelance"));
    }

    // --- Income.Deactivate ---

    [Test]
    public void Deactivate_SetsActiveFalse()
    {
        // Arrange
        var income = Income.Create(1L, "Salário", "recurring", 5000m, FixedNow);

        // Act
        income.Deactivate();

        // Assert
        Assert.That(income.Active, Is.False);
    }
}
