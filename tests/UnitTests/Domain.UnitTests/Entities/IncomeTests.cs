using Domain.Entities;
using Domain.Enums;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class IncomeTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    private static Income CreateIncome() =>
        Income.Create(1L, "Salário", IncomeKindEnum.Recurring, 5000m, FixedNow);

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Create_BlankName_ThrowsArgumentException(string? name) =>
        Assert.That(
            () => Income.Create(1L, name!, IncomeKindEnum.Recurring, 1000m, FixedNow),
            Throws.InstanceOf<ArgumentException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(
            () => Income.Create(ownerId, "Salário", IncomeKindEnum.Recurring, 1000m, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException() =>
        Assert.That(
            () => Income.Create(1L, "Salário", IncomeKindEnum.Recurring, -0.01m, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_ValidArgs_ReturnsActiveIncome()
    {
        // Act
        var income = CreateIncome();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(income.OwnerId, Is.EqualTo(1L));
            Assert.That(income.Name, Is.EqualTo("Salário"));
            Assert.That(income.Kind, Is.EqualTo(IncomeKindEnum.Recurring));
            Assert.That(income.DefaultAmount, Is.EqualTo(5000m));
            Assert.That(income.Active, Is.True);
            Assert.That(income.CreatedAt, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public void Create_ZeroDefaultAmount_IsAllowed()
    {
        // Act
        var income = Income.Create(1L, "Bônus eventual", IncomeKindEnum.OneOff, 0m, FixedNow);

        // Assert
        Assert.That(income.DefaultAmount, Is.Zero);
    }

    [TestCase(IncomeKindEnum.Recurring)]
    [TestCase(IncomeKindEnum.OneOff)]
    public void Create_AnyKind_SetsKind(IncomeKindEnum kind)
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
        var income = Income.Create(1L, "  Salário  ", IncomeKindEnum.Recurring, 5000m, FixedNow);

        // Assert
        Assert.That(income.Name, Is.EqualTo("Salário"));
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Update_BlankName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var income = CreateIncome();

        // Act / Assert
        Assert.That(
            () => income.Update(name!, IncomeKindEnum.Recurring, 5000m),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Update_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var income = CreateIncome();

        // Act / Assert
        Assert.That(
            () => income.Update("Salário", IncomeKindEnum.Recurring, -1m),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Update_ValidArgs_ChangesFieldsAndTrimsName()
    {
        // Arrange
        var income = CreateIncome();

        // Act
        income.Update("  Freelance  ", IncomeKindEnum.OneOff, 2500m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(income.Name, Is.EqualTo("Freelance"));
            Assert.That(income.Kind, Is.EqualTo(IncomeKindEnum.OneOff));
            Assert.That(income.DefaultAmount, Is.EqualTo(2500m));
        }
    }

    [Test]
    public void Deactivate_ActiveIncome_SetsActiveFalse()
    {
        // Arrange
        var income = CreateIncome();

        // Act
        income.Deactivate();

        // Assert
        Assert.That(income.Active, Is.False);
    }
}
