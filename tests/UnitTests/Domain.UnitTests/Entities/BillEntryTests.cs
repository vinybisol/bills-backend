using Domain.Entities;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class BillEntryTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static BillEntry CreateEntry(decimal planned = 1000m, decimal splitRatio = 1m, long? personId = null) =>
        BillEntry.Create(1L, 9L, 2026, 7, planned, splitRatio, personId, CreatedAt);

    // --- Create ---

    [Test]
    public void Create_ValidArgs_SnapshotsAllFieldsAndStartsUnpaid()
    {
        // Act
        var entry = BillEntry.Create(1L, 42L, 2025, 3, 120m, 0.5m, 99L, CreatedAt);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.OwnerId, Is.EqualTo(1L));
            Assert.That(entry.BillId, Is.EqualTo(42L));
            Assert.That(entry.RefYear, Is.EqualTo(2025));
            Assert.That(entry.RefMonth, Is.EqualTo(3));
            Assert.That(entry.PlannedAmount, Is.EqualTo(120m));
            Assert.That(entry.SplitRatioSnapshot, Is.EqualTo(0.5m));
            Assert.That(entry.PersonId, Is.EqualTo(99L));
            Assert.That(entry.ActualAmount, Is.Null);
            Assert.That(entry.Paid, Is.False);
            Assert.That(entry.PaidDate, Is.Null);
            Assert.That(entry.Received, Is.False);
            Assert.That(entry.ReceivedDate, Is.Null);
            Assert.That(entry.CreatedAt, Is.EqualTo(CreatedAt));
        }
    }

    [Test]
    public void Create_FullOwnership_HasNullPersonAndSplitOne()
    {
        // Act
        var entry = CreateEntry(planned: 500m, splitRatio: 1m, personId: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.PersonId, Is.Null);
            Assert.That(entry.SplitRatioSnapshot, Is.EqualTo(1m));
        }
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(
            () => BillEntry.Create(ownerId, 9L, 2026, 7, 100m, 1m, null, CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveBillId_ThrowsArgumentOutOfRangeException(long billId) =>
        Assert.That(
            () => BillEntry.Create(1L, billId, 2026, 7, 100m, 1m, null, CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_NegativePlannedAmount_ThrowsArgumentOutOfRangeException() =>
        Assert.That(() => CreateEntry(planned: -1m), Throws.InstanceOf<ArgumentOutOfRangeException>());

    // --- MarkPaid / Unfreeze ---

    [Test]
    public void MarkPaid_WithoutActualAmount_UsesPlannedAsActual()
    {
        // Arrange
        var entry = CreateEntry(planned: 1000m);

        // Act
        entry.MarkPaid(Now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Paid, Is.True);
            Assert.That(entry.ActualAmount, Is.EqualTo(1000m));
            Assert.That(entry.PaidDate, Is.EqualTo(Now));
        }
    }

    [Test]
    public void MarkPaid_WithActualAmount_RecordsActualAndKeepsPlanned()
    {
        // Arrange
        var entry = CreateEntry(planned: 1000m);

        // Act
        entry.MarkPaid(Now, actualAmount: 980m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Paid, Is.True);
            Assert.That(entry.ActualAmount, Is.EqualTo(980m));
            Assert.That(entry.PlannedAmount, Is.EqualTo(1000m));
        }
    }

    [Test]
    public void MarkPaid_DoesNotAffectReceivedFlag()
    {
        // Arrange
        var entry = CreateEntry(splitRatio: 0.5m, personId: 2L);

        // Act
        entry.MarkPaid(Now);

        // Assert — paid (I paid) and received (the person paid me back) are independent
        Assert.That(entry.Received, Is.False);
    }

    [Test]
    public void Unfreeze_PaidEntry_ClearsPaidAndPaidDateButKeepsActual()
    {
        // Arrange
        var entry = CreateEntry(planned: 1000m);
        entry.MarkPaid(Now, actualAmount: 990m);

        // Act
        entry.Unfreeze();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Paid, Is.False);
            Assert.That(entry.PaidDate, Is.Null);
            Assert.That(entry.ActualAmount, Is.EqualTo(990m));
        }
    }

    // --- MarkReceived / UnmarkReceived ---

    [Test]
    public void MarkReceived_SetsReceivedAndDateWithoutTouchingPaid()
    {
        // Arrange
        var entry = CreateEntry(splitRatio: 0.5m, personId: 2L);

        // Act
        entry.MarkReceived(Now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Received, Is.True);
            Assert.That(entry.ReceivedDate, Is.EqualTo(Now));
            Assert.That(entry.Paid, Is.False);
        }
    }

    [Test]
    public void UnmarkReceived_ReceivedEntry_ClearsReceivedAndDate()
    {
        // Arrange
        var entry = CreateEntry(splitRatio: 0.5m, personId: 2L);
        entry.MarkReceived(Now);

        // Act
        entry.UnmarkReceived();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Received, Is.False);
            Assert.That(entry.ReceivedDate, Is.Null);
        }
    }

    // --- UpdateAmounts ---

    [Test]
    public void UpdateAmounts_BothValues_UpdatesBoth()
    {
        // Arrange
        var entry = CreateEntry(planned: 1000m);

        // Act
        entry.UpdateAmounts(plannedAmount: 1100m, actualAmount: 1090m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(1100m));
            Assert.That(entry.ActualAmount, Is.EqualTo(1090m));
        }
    }

    [Test]
    public void UpdateAmounts_OnlyPlanned_LeavesActualUntouched()
    {
        // Arrange
        var entry = CreateEntry(planned: 1000m);

        // Act
        entry.UpdateAmounts(plannedAmount: 1200m, actualAmount: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(1200m));
            Assert.That(entry.ActualAmount, Is.Null);
        }
    }

    [Test]
    public void UpdateAmounts_OnlyActual_LeavesPlannedUntouched()
    {
        // Arrange
        var entry = CreateEntry(planned: 1000m);

        // Act
        entry.UpdateAmounts(plannedAmount: null, actualAmount: 950m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(1000m));
            Assert.That(entry.ActualAmount, Is.EqualTo(950m));
        }
    }

    [Test]
    public void UpdateAmounts_NegativePlanned_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var entry = CreateEntry();

        // Act / Assert
        Assert.That(
            () => entry.UpdateAmounts(plannedAmount: -1m, actualAmount: null),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void UpdateAmounts_NegativeActual_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var entry = CreateEntry();

        // Act / Assert
        Assert.That(
            () => entry.UpdateAmounts(plannedAmount: null, actualAmount: -1m),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // --- UpdatePlanned ---

    [Test]
    public void UpdatePlanned_NonNegativeAmount_UpdatesPlannedAmount()
    {
        // Arrange
        var entry = CreateEntry(planned: 100m);

        // Act
        entry.UpdatePlanned(175m);

        // Assert
        Assert.That(entry.PlannedAmount, Is.EqualTo(175m));
    }

    [Test]
    public void UpdatePlanned_NegativeAmount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var entry = CreateEntry(planned: 100m);

        // Act / Assert
        Assert.That(() => entry.UpdatePlanned(-1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
