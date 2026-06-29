using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="Bill"/> domain rules, including the split/person business rule.
/// </summary>
[TestFixture]
public sealed class BillTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 29, 21, 00, 00, TimeSpan.Zero);

    // --- Bill.Create guards ---

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_BlankName_ThrowsArgumentException(string? name)
    {
        Assert.That(
            () => Bill.Create(1L, name!, 1L, BillKind.Recurring, 500m, 1m, null, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId)
    {
        Assert.That(
            () => Bill.Create(ownerId, "Aluguel", 1L, BillKind.Recurring, 500m, 1m, null, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_NegativeDefaultAmount_ThrowsArgumentOutOfRangeException()
    {
        Assert.That(
            () => Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, -0.01m, 1m, null, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_SplitRatioBelow0_ThrowsArgumentOutOfRangeException()
    {
        Assert.That(
            () => Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, -0.01m, 2L, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_SplitRatioAbove1_ThrowsArgumentOutOfRangeException()
    {
        Assert.That(
            () => Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 1.01m, null, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_SplitRatioLessThan1_WithNullPersonId_ThrowsArgumentException()
    {
        Assert.That(
            () => Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 0.5m, null, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_SplitRatioEquals1_WithPersonId_ThrowsArgumentException()
    {
        Assert.That(
            () => Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 1m, 2L, FixedNow),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Create_SplitRatioLessThan1_WithPersonId_Succeeds()
    {
        // Arrange / Act
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 0.5m, 2L, FixedNow);

        // Assert
        Assert.That(bill.PersonId, Is.EqualTo(2L));
    }

    [Test]
    public void Create_SplitRatioEquals1_WithNullPersonId_Succeeds()
    {
        // Arrange / Act
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 1m, null, FixedNow);

        // Assert
        Assert.That(bill.PersonId, Is.Null);
    }

    [Test]
    public void Create_ValidArgs_ReturnsActiveBill()
    {
        // Arrange / Act
        var bill = Bill.Create(1L, "Aluguel", 2L, BillKind.Recurring, 1500m, 0.5m, 3L, FixedNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(bill.OwnerId, Is.EqualTo(1L));
            Assert.That(bill.Name, Is.EqualTo("Aluguel"));
            Assert.That(bill.CategoryId, Is.EqualTo(2L));
            Assert.That(bill.Kind, Is.EqualTo(BillKind.Recurring));
            Assert.That(bill.DefaultAmount, Is.EqualTo(1500m));
            Assert.That(bill.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(bill.PersonId, Is.EqualTo(3L));
            Assert.That(bill.Active, Is.True);
            Assert.That(bill.CreatedAt, Is.EqualTo(FixedNow));
        });
    }

    [Test]
    public void Create_NameWithSurroundingWhitespace_TrimsName()
    {
        // Arrange / Act
        var bill = Bill.Create(1L, "  Aluguel  ", 1L, BillKind.Recurring, 500m, 1m, null, FixedNow);

        // Assert
        Assert.That(bill.Name, Is.EqualTo("Aluguel"));
    }

    [Test]
    public void Create_ZeroDefaultAmount_IsAllowed()
    {
        // Arrange / Act
        var bill = Bill.Create(1L, "Eventual", 1L, BillKind.OneOff, 0m, 1m, null, FixedNow);

        // Assert
        Assert.That(bill.DefaultAmount, Is.EqualTo(0m));
    }

    // --- Bill.Update ---

    [Test]
    public void Update_ChangesAllFields()
    {
        // Arrange
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 1500m, 0.5m, 2L, FixedNow);

        // Act
        bill.Update("Internet", 3L, BillKind.OneOff, 100m, 1m, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(bill.Name, Is.EqualTo("Internet"));
            Assert.That(bill.CategoryId, Is.EqualTo(3L));
            Assert.That(bill.Kind, Is.EqualTo(BillKind.OneOff));
            Assert.That(bill.DefaultAmount, Is.EqualTo(100m));
            Assert.That(bill.SplitRatio, Is.EqualTo(1m));
            Assert.That(bill.PersonId, Is.Null);
        });
    }

    [Test]
    public void Update_InvalidSplitPersonCombination_ThrowsArgumentException()
    {
        // Arrange
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 0.5m, 2L, FixedNow);

        // Act / Assert
        Assert.That(
            () => bill.Update("Aluguel", 1L, BillKind.Recurring, 500m, 0.5m, null),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Update_NameWithSurroundingWhitespace_TrimsName()
    {
        // Arrange
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 1m, null, FixedNow);

        // Act
        bill.Update("  Internet  ", 1L, BillKind.Recurring, 500m, 1m, null);

        // Assert
        Assert.That(bill.Name, Is.EqualTo("Internet"));
    }

    // --- Bill.Deactivate ---

    [Test]
    public void Deactivate_SetsActiveFalse()
    {
        // Arrange
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 500m, 1m, null, FixedNow);

        // Act
        bill.Deactivate();

        // Assert
        Assert.That(bill.Active, Is.False);
    }
}
