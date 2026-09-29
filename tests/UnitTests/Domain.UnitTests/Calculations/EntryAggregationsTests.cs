using Domain.Calculations;
using Domain.Entities;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Calculations;

[TestFixture]
public sealed class EntryAggregationsTests
{
    // --- EffectiveMyShare / EffectiveReceivable ---

    [TestCase(1, 100, 0)]
    [TestCase(0.5, 50, 50)]
    [TestCase(0, 0, 100)]
    public void EffectiveShares_NoActual_SplitsPlannedAmount(decimal split, decimal expectedMine, decimal expectedReceivable)
    {
        // Arrange
        var entry = Entries.Bill(planned: 100m, split: split);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EntryAggregations.EffectiveMyShare(entry), Is.EqualTo(expectedMine));
            Assert.That(EntryAggregations.EffectiveReceivable(entry), Is.EqualTo(expectedReceivable));
        }
    }

    [Test]
    public void EffectiveShares_WithActual_SplitsActualAmount()
    {
        // Arrange
        var entry = Entries.Bill(planned: 100m, split: 0.5m, actual: 80m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EntryAggregations.EffectiveMyShare(entry), Is.EqualTo(40m));
            Assert.That(EntryAggregations.EffectiveReceivable(entry), Is.EqualTo(40m));
        }
    }

    // --- Bill sums: empty ---

    [Test]
    public void BillSums_NoEntries_AreZero()
    {
        var none = Array.Empty<BillEntry>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EntryAggregations.PlannedMyShare(none), Is.Zero);
            Assert.That(EntryAggregations.ActualMyShare(none), Is.Zero);
            Assert.That(EntryAggregations.ReceivablePending(none), Is.Zero);
            Assert.That(EntryAggregations.ReceivableReceived(none), Is.Zero);
            Assert.That(EntryAggregations.PaidFull(none), Is.Zero);
        }
    }

    // --- PlannedMyShare ---

    [Test]
    public void PlannedMyShare_MixedEntries_UsesPlannedAmountEvenWhenActualDiffers()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, split: 1m, actual: 150m, paid: true),
            Entries.Bill(planned: 200m, split: 0.5m),
            Entries.Bill(planned: 300m, split: 0m),
        ];

        // Act & Assert: 100 + 100 + 0
        Assert.That(EntryAggregations.PlannedMyShare(entries), Is.EqualTo(200m));
    }

    // --- ActualMyShare ---

    [Test]
    public void ActualMyShare_MixedEntries_SumsEffectiveShareOfPaidOnly()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, split: 0.5m, actual: 120m, paid: true),
            Entries.Bill(planned: 50m, split: 1m, paid: true),
            Entries.Bill(planned: 999m, split: 1m, actual: 10m),
        ];

        // Act & Assert: 60 + 50; the unpaid entry is ignored even with an actual amount
        Assert.That(EntryAggregations.ActualMyShare(entries), Is.EqualTo(110m));
    }

    [Test]
    public void ActualMyShare_PaidWithZeroActual_CountsZeroNotPlanned() =>
        Assert.That(
            EntryAggregations.ActualMyShare([Entries.Bill(planned: 100m, actual: 0m, paid: true)]),
            Is.Zero);

    // --- ReceivablePending / ReceivableReceived ---

    [Test]
    public void Receivables_MixedEntries_SplitsPendingAndReceived()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, split: 0.5m),
            Entries.Bill(planned: 200m, split: 0m, actual: 220m, paid: true),
            Entries.Bill(planned: 80m, split: 0.5m, received: true),
            Entries.Bill(planned: 500m, split: 1m),
        ];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EntryAggregations.ReceivablePending(entries), Is.EqualTo(270m)); // 50 + 220 + 0
            Assert.That(EntryAggregations.ReceivableReceived(entries), Is.EqualTo(40m));
        }
    }

    // --- PaidFull ---

    [Test]
    public void PaidFull_MixedEntries_SumsFullEffectiveAmountOfPaidOnly()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, split: 0.5m, actual: 90m, paid: true),
            Entries.Bill(planned: 40m, split: 0m, paid: true),
            Entries.Bill(planned: 1000m, split: 1m),
        ];

        // Act & Assert: split does not matter — the full amount left the account
        Assert.That(EntryAggregations.PaidFull(entries), Is.EqualTo(130m));
    }

    // --- Income sums ---

    [Test]
    public void IncomeSums_NoEntries_AreZero()
    {
        var none = Array.Empty<IncomeEntry>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EntryAggregations.PlannedIncome(none), Is.Zero);
            Assert.That(EntryAggregations.ReceivedIncome(none), Is.Zero);
        }
    }

    [Test]
    public void IncomeSums_MixedEntries_PlannedSumsAllAndReceivedSumsEffectiveOfReceivedOnly()
    {
        // Arrange
        IncomeEntry[] entries =
        [
            Entries.Income(planned: 5000m, actual: 5200m, received: true),
            Entries.Income(planned: 1000m, received: true),
            Entries.Income(planned: 300m, actual: 250m),
        ];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EntryAggregations.PlannedIncome(entries), Is.EqualTo(6300m));
            Assert.That(EntryAggregations.ReceivedIncome(entries), Is.EqualTo(6200m));
        }
    }

    // --- SummarizeReceivables ---

    [Test]
    public void SummarizeReceivables_NoEntries_ReturnsZeros() =>
        Assert.That(EntryAggregations.SummarizeReceivables([]), Is.EqualTo(new ReceivableTotals(0m, 0m, 0m)));

    [Test]
    public void SummarizeReceivables_MixedEntries_TotalEqualsReceivedPlusPending()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, split: 0.5m, received: true),
            Entries.Bill(planned: 60m, split: 0m, actual: 80m),
            Entries.Bill(planned: 30m, split: 0.5m),
        ];

        // Act
        var totals = EntryAggregations.SummarizeReceivables(entries);

        // Assert
        Assert.That(totals, Is.EqualTo(new ReceivableTotals(Total: 145m, Received: 50m, Pending: 95m)));
    }

    [Test]
    public void SummarizeReceivables_LazySequence_EnumeratesConsistently()
    {
        // Arrange
        var source = new[] { Entries.Bill(planned: 100m, split: 0.5m, received: true), Entries.Bill(planned: 10m, split: 0m) };

        // Act
        var totals = EntryAggregations.SummarizeReceivables(source.Where(_ => true));

        // Assert
        Assert.That(totals, Is.EqualTo(new ReceivableTotals(60m, 50m, 10m)));
    }

    // --- FilterByReceivedStatus ---

    [TestCase("received", 1)]
    [TestCase("pending", 2)]
    [TestCase(null, 3)]
    [TestCase("", 3)]
    [TestCase("all", 3)]
    [TestCase("RECEIVED", 3)]
    public void FilterByReceivedStatus_Status_KeepsExpectedCount(string? status, int expectedCount)
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 10m, split: 0.5m, received: true),
            Entries.Bill(planned: 20m, split: 0.5m),
            Entries.Bill(planned: 30m, split: 0m),
        ];

        // Act & Assert
        Assert.That(EntryAggregations.FilterByReceivedStatus(entries, status).Count(), Is.EqualTo(expectedCount));
    }

    [Test]
    public void FilterByReceivedStatus_Received_KeepsOnlyReceivedEntries()
    {
        BillEntry[] entries = [Entries.Bill(planned: 10m, split: 0.5m, received: true), Entries.Bill(planned: 20m, split: 0.5m)];

        Assert.That(EntryAggregations.FilterByReceivedStatus(entries, "received"), Has.All.Matches<BillEntry>(e => e.Received));
    }

    // --- SummarizeByCategory ---

    [Test]
    public void SummarizeByCategory_NoEntries_ReturnsEmpty() =>
        Assert.That(EntryAggregations.SummarizeByCategory([], _ => 1L), Is.Empty);

    [Test]
    public void SummarizeByCategory_MultipleCategories_GroupsAndOrdersByPlannedShareDescending()
    {
        // Arrange: bill 1 -> category 10, bill 2 -> category 20
        var categoryOfBill = new Dictionary<long, long> { [1L] = 10L, [2L] = 20L };
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, split: 1m, billId: 1L, actual: 90m, paid: true),
            Entries.Bill(planned: 50m, split: 0.5m, billId: 1L),
            Entries.Bill(planned: 400m, split: 0.5m, billId: 2L, actual: 500m, paid: true),
        ];

        // Act
        var result = EntryAggregations.SummarizeByCategory(entries, e => categoryOfBill[e.BillId]);

        // Assert
        Assert.That(result, Is.EqualTo(new[]
        {
            new CategoryShare(20L, PlannedMyShare: 200m, ActualMyShare: 250m),
            new CategoryShare(10L, PlannedMyShare: 125m, ActualMyShare: 90m),
        }));
    }

    [Test]
    public void SummarizeByCategory_TiedPlannedShare_KeepsFirstAppearanceOrder()
    {
        // Arrange
        BillEntry[] entries =
        [
            Entries.Bill(planned: 100m, billId: 7L),
            Entries.Bill(planned: 100m, billId: 3L),
        ];

        // Act
        var result = EntryAggregations.SummarizeByCategory(entries, e => e.BillId);

        // Assert
        Assert.That(result.Select(c => c.CategoryId), Is.EqualTo(new[] { 7L, 3L }));
    }

    // --- SummarizeByMonth ---

    [Test]
    public void SummarizeByMonth_NoEntries_ReturnsTwelveZeroedMonths()
    {
        // Act
        var months = EntryAggregations.SummarizeByMonth([], []);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(months.Select(m => m.Month), Is.EqualTo(Enumerable.Range(1, 12)));
            Assert.That(months, Has.All.Matches<MonthSummary>(m =>
                m.PlannedExpense == 0m && m.ActualExpense == 0m && m.PlannedIncome == 0m && m.ActualIncome == 0m &&
                m.PlannedBalance == 0m && m.ActualBalance == 0m));
        }
    }

    [Test]
    public void SummarizeByMonth_EntriesInTwoMonths_AggregatesEachMonthIndependently()
    {
        // Arrange
        BillEntry[] bills =
        [
            Entries.Bill(planned: 100m, split: 0.5m, actual: 120m, paid: true, month: 1),
            Entries.Bill(planned: 40m, split: 1m, month: 1),
            Entries.Bill(planned: 300m, split: 1m, month: 12),
        ];
        IncomeEntry[] incomes =
        [
            Entries.Income(planned: 1000m, actual: 900m, received: true, month: 1),
            Entries.Income(planned: 500m, month: 12),
        ];

        // Act
        var months = EntryAggregations.SummarizeByMonth(bills, incomes);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(months, Has.Count.EqualTo(12));
            Assert.That(months[0], Is.EqualTo(new MonthSummary(1, PlannedExpense: 90m, ActualExpense: 60m, PlannedIncome: 1000m, ActualIncome: 900m)));
            Assert.That(months[0].PlannedBalance, Is.EqualTo(910m));
            Assert.That(months[0].ActualBalance, Is.EqualTo(840m));
            Assert.That(months[11], Is.EqualTo(new MonthSummary(12, 300m, 0m, 500m, 0m)));
            Assert.That(months[11].PlannedBalance, Is.EqualTo(200m));
            Assert.That(months[11].ActualBalance, Is.Zero);
            Assert.That(months[5], Is.EqualTo(new MonthSummary(6, 0m, 0m, 0m, 0m)));
        }
    }
}
