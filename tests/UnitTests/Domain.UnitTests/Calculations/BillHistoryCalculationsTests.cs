using Domain.Calculations;
using Domain.Entities;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Calculations;

[TestFixture]
public sealed class BillHistoryCalculationsTests
{
    // --- BuildItems ---

    [Test]
    public void BuildItems_NoEntries_ReturnsEmpty() =>
        Assert.That(BillHistoryCalculations.BuildItems([]), Is.Empty);

    [Test]
    public void BuildItems_UnorderedEntries_OrdersChronologically()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 3m, year: 2026, month: 1),
            Entries.Bill(planned: 1m, year: 2025, month: 11),
            Entries.Bill(planned: 2m, year: 2025, month: 12),
        ];

        // Act
        var items = BillHistoryCalculations.BuildItems(entries);

        // Assert
        Assert.That(items.Select(i => (i.Year, i.Month)), Is.EqualTo(new[] { (2025, 11), (2025, 12), (2026, 1) }));
    }

    [Test]
    public void BuildItems_FirstItem_HasNoVariation() =>
        Assert.That(BillHistoryCalculations.BuildItems([Entries.Bill(planned: 100m)])[0].Variation, Is.Null);

    [Test]
    public void BuildItems_Series_ComputesEffectiveMyShareAndVariationFromPreviousEffective()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, split: 0.5m, actual: 150m, paid: true, month: 1),
            Entries.Bill(planned: 100m, split: 0.5m, month: 2),
            Entries.Bill(planned: 0m, split: 0.5m, month: 3),
            Entries.Bill(planned: 50m, split: 0.5m, month: 4),
        ];

        // Act
        var items = BillHistoryCalculations.BuildItems(entries);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0], Is.EqualTo(new BillHistoryItem(
                2026, 1, 100m, 150m, Effective: 150m, MyShare: 75m, Paid: true, PaidDate: Entries.PaidAt, Variation: null)));
            Assert.That(items[1].Effective, Is.EqualTo(100m));
            Assert.That(items[1].MyShare, Is.EqualTo(50m));
            Assert.That(items[1].Variation, Is.EqualTo(new EntryCalculations.Variation(-50m, -33.33m)));
            Assert.That(items[2].Variation, Is.EqualTo(new EntryCalculations.Variation(-100m, -100m)));
            Assert.That(items[3].Variation, Is.EqualTo(new EntryCalculations.Variation(50m, null)));
        }
    }

    [TestCase(1, 100)]
    [TestCase(0.5, 50)]
    [TestCase(0, 0)]
    public void BuildItems_SplitRatio_MyShareIsOwnersFractionOfEffective(decimal split, decimal expectedMyShare) =>
        Assert.That(BillHistoryCalculations.BuildItems([Entries.Bill(planned: 100m, split: split)])[0].MyShare, Is.EqualTo(expectedMyShare));

    // --- Summarize ---

    [Test]
    public void Summarize_NoItems_ReturnsZeros() =>
        Assert.That(BillHistoryCalculations.Summarize([]), Is.EqualTo(new BillHistorySummary(0m, 0m, 0m, 0m)));

    [Test]
    public void Summarize_MixedItems_ComputesAverageMinMaxAndPaidMyShare()
    {
        // Arrange
        var items = BillHistoryCalculations.BuildItems(
        [
            Entries.Bill(planned: 100m, split: 0.5m, actual: 120m, paid: true, month: 1),
            Entries.Bill(planned: 90m, split: 0.5m, month: 2),
            Entries.Bill(planned: 60m, split: 0.5m, paid: true, month: 3),
        ]);

        // Act
        var summary = BillHistoryCalculations.Summarize(items);

        // Assert
        Assert.That(summary, Is.EqualTo(new BillHistorySummary(
            AverageEffective: 90m, MinEffective: 60m, MaxEffective: 120m, TotalPaidMyShare: 90m)));
    }
}
