using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for one-off entry creation: snapshot behaviour and domain validation.
/// </summary>
[TestFixture]
public sealed class OneOffEntryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 06, 30, 12, 00, 00, TimeSpan.Zero);

    // --- BillEntry snapshot ---

    [Test]
    public void BillEntry_Create_SnapshotsSplitRatioAndPersonId()
    {
        // Arrange
        const decimal splitRatio = 0.5m;
        const long personId = 42L;

        // Act
        var entry = BillEntry.Create(1L, 9L, 2026, 4, 1200m, splitRatio, personId, FixedNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.SplitRatioSnapshot, Is.EqualTo(splitRatio));
            Assert.That(entry.PersonId, Is.EqualTo(personId));
            Assert.That(entry.PlannedAmount, Is.EqualTo(1200m));
            Assert.That(entry.Paid, Is.False);
            Assert.That(entry.Received, Is.False);
        });
    }

    [Test]
    public void BillEntry_Create_FullOwnership_PersonIdIsNull()
    {
        // Act
        var entry = BillEntry.Create(1L, 9L, 2026, 4, 500m, 1m, null, FixedNow);

        // Assert
        Assert.That(entry.PersonId, Is.Null);
        Assert.That(entry.SplitRatioSnapshot, Is.EqualTo(1m));
    }

    [Test]
    public void BillEntry_Create_NegativePlannedAmount_Throws()
    {
        Assert.That(
            () => BillEntry.Create(1L, 9L, 2026, 4, -1m, 1m, null, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // --- IncomeEntry snapshot ---

    [Test]
    public void IncomeEntry_Create_SnapshotsPlannedAmount()
    {
        // Act
        var entry = IncomeEntry.Create(1L, 5L, 2026, 4, 800m, FixedNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(800m));
            Assert.That(entry.Received, Is.False);
            Assert.That(entry.ActualAmount, Is.Null);
        });
    }

    [Test]
    public void IncomeEntry_Create_NegativePlannedAmount_Throws()
    {
        Assert.That(
            () => IncomeEntry.Create(1L, 5L, 2026, 4, -1m, FixedNow),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
