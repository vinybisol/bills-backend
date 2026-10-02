using Domain.Calculations;
using Domain.Entities;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Calculations;

[TestFixture]
public sealed class BillRecalculationTests
{
    [Test]
    public void ApplyToEntries_NoEntries_ReturnsZeroCounts() =>
        Assert.That(BillRecalculation.ApplyToEntries([], 100m), Is.EqualTo(new RecalculationResult(0, 0)));

    [Test]
    public void ApplyToEntries_MixedEntries_UpdatesUnpaidAndSkipsPaid()
    {
        // Arrange
        var unpaid1 = Entries.Bill(planned: 100m, month: 7);
        var paid = Entries.Bill(planned: 100m, paid: true, month: 8);
        var unpaid2 = Entries.Bill(planned: 100m, actual: 90m, month: 9);

        // Act
        var result = BillRecalculation.ApplyToEntries([unpaid1, paid, unpaid2], 150m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(new RecalculationResult(UpdatedEntries: 2, SkippedPaid: 1)));
            Assert.That(unpaid1.PlannedAmount, Is.EqualTo(150m));
            Assert.That(unpaid2.PlannedAmount, Is.EqualTo(150m));
            Assert.That(unpaid2.ActualAmount, Is.EqualTo(90m));
            Assert.That(paid.PlannedAmount, Is.EqualTo(100m));
            Assert.That(paid.ActualAmount, Is.EqualTo(100m));
        }
    }

    [Test]
    public void ApplyToEntries_ZeroAmount_SetsPlannedToZero()
    {
        // Arrange
        var entry = Entries.Bill(planned: 100m);

        // Act
        var result = BillRecalculation.ApplyToEntries([entry], 0m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(new RecalculationResult(1, 0)));
            Assert.That(entry.PlannedAmount, Is.Zero);
        }
    }

    [Test]
    public void ApplyToEntries_AllPaid_UpdatesNothing()
    {
        // Arrange
        BillEntry[] entries = [Entries.Bill(planned: 10m, paid: true), Entries.Bill(planned: 20m, paid: true)];

        // Act
        var result = BillRecalculation.ApplyToEntries(entries, 99m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(new RecalculationResult(0, 2)));
            Assert.That(entries.Select(e => e.PlannedAmount), Is.EqualTo(new[] { 10m, 20m }));
        }
    }

    [Test]
    public void ApplyToEntries_NegativeAmount_ThrowsArgumentOutOfRangeException() =>
        Assert.That(
            () => BillRecalculation.ApplyToEntries([Entries.Bill(planned: 10m)], -1m),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
}
