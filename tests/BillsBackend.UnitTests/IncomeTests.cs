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
            () => Income.Create(1L, name!, IncomeKind.Recurring, 1000m, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException()
    {
        Assert.That(
            () => Income.Create(1L, "Salário", IncomeKind.Recurring, -0.01m, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId)
    {
        Assert.That(
            () => Income.Create(ownerId, "Salário", IncomeKind.Recurring, 1000m, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_ValidArgs_ReturnsActiveIncome()
    {
        var income = Income.Create(1L, "Salário", IncomeKind.Recurring, 5000m, FixedNow);

        Assert.Multiple(() =>
        {
            Assert.That(income.OwnerId, Is.EqualTo(1L));
            Assert.That(income.Name, Is.EqualTo("Salário"));
            Assert.That(income.Kind, Is.EqualTo(IncomeKind.Recurring));
            Assert.That(income.DefaultAmount, Is.EqualTo(5000m));
            Assert.That(income.Active, Is.True);
            Assert.That(income.CreatedAt, Is.EqualTo(FixedNow));
        });
    }

    [Test]
    public void Create_ZeroDefaultAmount_IsAllowed()
    {
        var income = Income.Create(1L, "Bônus eventual", IncomeKind.OneOff, 0m, FixedNow);

        Assert.That(income.DefaultAmount, Is.EqualTo(0m));
    }

    [TestCase(IncomeKind.Recurring)]
    [TestCase(IncomeKind.OneOff)]
    public void Create_ValidKind_SetsKind(IncomeKind kind)
    {
        var income = Income.Create(1L, "Renda", kind, 100m, FixedNow);

        Assert.That(income.Kind, Is.EqualTo(kind));
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        var income = Income.Create(1L, "  Salário  ", IncomeKind.Recurring, 5000m, FixedNow);

        Assert.That(income.Name, Is.EqualTo("Salário"));
    }

    // --- Income.Update ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Update_BlankName_ThrowsArgumentException(string? name)
    {
        var income = Income.Create(1L, "Salário", IncomeKind.Recurring, 5000m, FixedNow);

        Assert.That(
            () => income.Update(name!, IncomeKind.Recurring, 5000m),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Update_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException()
    {
        var income = Income.Create(1L, "Salário", IncomeKind.Recurring, 5000m, FixedNow);

        Assert.That(
            () => income.Update("Salário", IncomeKind.Recurring, -1m),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Update_ChangesFields()
    {
        var income = Income.Create(1L, "Salário", IncomeKind.Recurring, 5000m, FixedNow);

        income.Update("Freelance", IncomeKind.OneOff, 2500m);

        Assert.Multiple(() =>
        {
            Assert.That(income.Name, Is.EqualTo("Freelance"));
            Assert.That(income.Kind, Is.EqualTo(IncomeKind.OneOff));
            Assert.That(income.DefaultAmount, Is.EqualTo(2500m));
        });
    }

    [Test]
    public void Update_NameWithSurroundingWhitespace_TrimsName()
    {
        var income = Income.Create(1L, "Salário", IncomeKind.Recurring, 5000m, FixedNow);

        income.Update("  Freelance  ", IncomeKind.OneOff, 2500m);

        Assert.That(income.Name, Is.EqualTo("Freelance"));
    }

    // --- Income.Deactivate ---

    [Test]
    public void Deactivate_SetsActiveFalse()
    {
        var income = Income.Create(1L, "Salário", IncomeKind.Recurring, 5000m, FixedNow);

        income.Deactivate();

        Assert.That(income.Active, Is.False);
    }
}
