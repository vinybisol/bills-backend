using Domain.Entities;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class IncomeEntryTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static IncomeEntry CreateEntry(decimal planned = 5000m) =>
        IncomeEntry.Create(1L, 3L, 2026, 7, planned, CreatedAt);

    // --- Create ---

    [Test]
    public void Create_ValidArgs_SnapshotsFieldsAndStartsNotReceived()
    {
        // Act
        var entry = IncomeEntry.Create(1L, 5L, 2026, 4, 800m, CreatedAt);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.OwnerId, Is.EqualTo(1L));
            Assert.That(entry.IncomeId, Is.EqualTo(5L));
            Assert.That(entry.RefYear, Is.EqualTo(2026));
            Assert.That(entry.RefMonth, Is.EqualTo(4));
            Assert.That(entry.PlannedAmount, Is.EqualTo(800m));
            Assert.That(entry.ActualAmount, Is.Null);
            Assert.That(entry.Received, Is.False);
            Assert.That(entry.ReceivedDate, Is.Null);
            Assert.That(entry.CreatedAt, Is.EqualTo(CreatedAt));
        }
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(
            () => IncomeEntry.Create(ownerId, 5L, 2026, 4, 800m, CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveIncomeId_ThrowsArgumentOutOfRangeException(long incomeId) =>
        Assert.That(
            () => IncomeEntry.Create(1L, incomeId, 2026, 4, 800m, CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_NegativePlannedAmount_ThrowsArgumentOutOfRangeException() =>
        Assert.That(() => CreateEntry(planned: -1m), Throws.InstanceOf<ArgumentOutOfRangeException>());

    // --- MarkReceived / Unfreeze ---

    [Test]
    public void MarkReceived_WithoutActualAmount_UsesPlannedAsActual()
    {
        // Arrange
        var entry = CreateEntry(planned: 5000m);

        // Act
        entry.MarkReceived(Now);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Received, Is.True);
            Assert.That(entry.ActualAmount, Is.EqualTo(5000m));
            Assert.That(entry.ReceivedDate, Is.EqualTo(Now));
        }
    }

    [Test]
    public void MarkReceived_WithActualAmount_RecordsActual()
    {
        // Arrange
        var entry = CreateEntry(planned: 5000m);

        // Act
        entry.MarkReceived(Now, actualAmount: 5200m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ActualAmount, Is.EqualTo(5200m));
            Assert.That(entry.PlannedAmount, Is.EqualTo(5000m));
        }
    }

    [Test]
    public void Unfreeze_ReceivedEntry_ClearsReceivedAndDate()
    {
        // Arrange
        var entry = CreateEntry();
        entry.MarkReceived(Now);

        // Act
        entry.Unfreeze();

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
        var entry = CreateEntry(planned: 5000m);

        // Act
        entry.UpdateAmounts(plannedAmount: 5500m, actualAmount: 5300m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(5500m));
            Assert.That(entry.ActualAmount, Is.EqualTo(5300m));
        }
    }

    [Test]
    public void UpdateAmounts_NullValues_ChangesNothing()
    {
        // Arrange
        var entry = CreateEntry(planned: 5000m);

        // Act
        entry.UpdateAmounts(plannedAmount: null, actualAmount: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(5000m));
            Assert.That(entry.ActualAmount, Is.Null);
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
}
