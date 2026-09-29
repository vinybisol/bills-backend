using Domain.Entities;
using Domain.Enums;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class BillTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 21, 0, 0, TimeSpan.Zero);

    private static Bill CreateBill(decimal defaultAmount = 500m, decimal splitRatio = 1m, long? personId = null) =>
        Bill.Create(1L, "Aluguel", 1L, BillKindEnum.Recurring, defaultAmount, splitRatio, personId, FixedNow);

    // --- Create ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Create_BlankName_ThrowsArgumentException(string? name) =>
        Assert.That(
            () => Bill.Create(1L, name!, 1L, BillKindEnum.Recurring, 500m, 1m, null, FixedNow),
            Throws.InstanceOf<ArgumentException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(
            () => Bill.Create(ownerId, "Aluguel", 1L, BillKindEnum.Recurring, 500m, 1m, null, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException() =>
        Assert.That(() => CreateBill(defaultAmount: -0.01m), Throws.InstanceOf<ArgumentOutOfRangeException>());

    [TestCase(-0.01)]
    [TestCase(1.01)]
    public void Create_SplitRatioOutsideZeroToOne_ThrowsArgumentOutOfRangeException(decimal splitRatio) =>
        Assert.That(
            () => CreateBill(splitRatio: splitRatio, personId: 2L),
            Throws.InstanceOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("splitRatio"));

    [TestCase(0.5)]
    [TestCase(0)]
    public void Create_SplitRatioBelowOneWithoutPerson_ThrowsArgumentException(decimal splitRatio) =>
        Assert.That(
            () => CreateBill(splitRatio: splitRatio, personId: null),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("personId"));

    [Test]
    public void Create_SplitRatioOneWithPerson_ThrowsArgumentException() =>
        Assert.That(
            () => CreateBill(splitRatio: 1m, personId: 2L),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("personId"));

    [TestCase(0.5)]
    [TestCase(0)]
    public void Create_SplitRatioBelowOneWithPerson_KeepsPerson(decimal splitRatio)
    {
        // Act
        var bill = CreateBill(splitRatio: splitRatio, personId: 2L);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bill.SplitRatio, Is.EqualTo(splitRatio));
            Assert.That(bill.PersonId, Is.EqualTo(2L));
        }
    }

    [Test]
    public void Create_SplitRatioOneWithoutPerson_HasNullPerson()
    {
        // Act
        var bill = CreateBill(splitRatio: 1m, personId: null);

        // Assert
        Assert.That(bill.PersonId, Is.Null);
    }

    [Test]
    public void Create_ValidArgs_ReturnsActiveBillWithAllFields()
    {
        // Act
        var bill = Bill.Create(1L, "Aluguel", 2L, BillKindEnum.Recurring, 1500m, 0.5m, 3L, FixedNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bill.OwnerId, Is.EqualTo(1L));
            Assert.That(bill.Name, Is.EqualTo("Aluguel"));
            Assert.That(bill.CategoryId, Is.EqualTo(2L));
            Assert.That(bill.Kind, Is.EqualTo(BillKindEnum.Recurring));
            Assert.That(bill.DefaultAmount, Is.EqualTo(1500m));
            Assert.That(bill.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(bill.PersonId, Is.EqualTo(3L));
            Assert.That(bill.Active, Is.True);
            Assert.That(bill.CreatedAt, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        // Act
        var bill = Bill.Create(1L, "  Aluguel  ", 1L, BillKindEnum.Recurring, 500m, 1m, null, FixedNow);

        // Assert
        Assert.That(bill.Name, Is.EqualTo("Aluguel"));
    }

    [Test]
    public void Create_ZeroDefaultAmount_IsAllowed()
    {
        // Act
        var bill = Bill.Create(1L, "Eventual", 1L, BillKindEnum.OneOff, 0m, 1m, null, FixedNow);

        // Assert
        Assert.That(bill.DefaultAmount, Is.Zero);
    }

    // --- Update ---

    [Test]
    public void Update_ValidArgs_ChangesAllEditableFields()
    {
        // Arrange
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKindEnum.Recurring, 1500m, 0.5m, 2L, FixedNow);

        // Act
        bill.Update("  Internet  ", 3L, BillKindEnum.OneOff, 100m, 1m, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bill.Name, Is.EqualTo("Internet"));
            Assert.That(bill.CategoryId, Is.EqualTo(3L));
            Assert.That(bill.Kind, Is.EqualTo(BillKindEnum.OneOff));
            Assert.That(bill.DefaultAmount, Is.EqualTo(100m));
            Assert.That(bill.SplitRatio, Is.EqualTo(1m));
            Assert.That(bill.PersonId, Is.Null);
            Assert.That(bill.OwnerId, Is.EqualTo(1L));
            Assert.That(bill.Active, Is.True);
        }
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Update_BlankName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var bill = CreateBill();

        // Act / Assert
        Assert.That(
            () => bill.Update(name!, 1L, BillKindEnum.Recurring, 500m, 1m, null),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Update_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var bill = CreateBill();

        // Act / Assert
        Assert.That(
            () => bill.Update("Aluguel", 1L, BillKindEnum.Recurring, -1m, 1m, null),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(-0.01)]
    [TestCase(1.01)]
    public void Update_SplitRatioOutsideZeroToOne_ThrowsArgumentOutOfRangeException(decimal splitRatio)
    {
        // Arrange
        var bill = CreateBill();

        // Act / Assert
        Assert.That(
            () => bill.Update("Aluguel", 1L, BillKindEnum.Recurring, 500m, splitRatio, 2L),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Update_SplitBelowOneWithoutPerson_ThrowsAndKeepsOriginalState()
    {
        // Arrange
        var bill = CreateBill(splitRatio: 0.5m, personId: 2L);

        // Act
        Assert.That(
            () => bill.Update("Outro", 1L, BillKindEnum.Recurring, 500m, 0.5m, null),
            Throws.ArgumentException);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bill.Name, Is.EqualTo("Aluguel"));
            Assert.That(bill.PersonId, Is.EqualTo(2L));
        }
    }

    [Test]
    public void Update_SplitOneWithPerson_ThrowsArgumentException()
    {
        // Arrange
        var bill = CreateBill();

        // Act / Assert
        Assert.That(
            () => bill.Update("Aluguel", 1L, BillKindEnum.Recurring, 500m, 1m, 2L),
            Throws.ArgumentException);
    }

    // --- Recalculate ---

    [TestCase(175)]
    [TestCase(0)]
    public void Recalculate_NonNegativeAmount_UpdatesDefaultAmount(decimal newAmount)
    {
        // Arrange
        var bill = CreateBill(defaultAmount: 100m);

        // Act
        bill.Recalculate(newAmount);

        // Assert
        Assert.That(bill.DefaultAmount, Is.EqualTo(newAmount));
    }

    [Test]
    public void Recalculate_NegativeAmount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var bill = CreateBill(defaultAmount: 100m);

        // Act / Assert
        Assert.That(() => bill.Recalculate(-1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // --- Deactivate ---

    [Test]
    public void Deactivate_ActiveBill_SetsActiveFalse()
    {
        // Arrange
        var bill = CreateBill();

        // Act
        bill.Deactivate();

        // Assert
        Assert.That(bill.Active, Is.False);
    }
}
