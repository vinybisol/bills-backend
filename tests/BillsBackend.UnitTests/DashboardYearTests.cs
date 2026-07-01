using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the dashboard-year aggregation logic used by <c>GET /api/dashboard/year</c>.
/// <para>
/// This codebase does not extract handler bodies into separately-testable classes, so these
/// tests construct real <see cref="BillEntry"/>/<see cref="IncomeEntry"/>/<see cref="Bill"/>/
/// <see cref="Category"/> domain objects and run the same LINQ grouping/filtering the endpoint
/// performs, asserting on the result.
/// </para>
/// <list type="bullet">
///   <item>months is always a 12-position series (month 1..12), zeroed when a month has no data</item>
///   <item>per-month plannedExpense/actualExpense/plannedIncome/actualIncome mirror dashboard/month's rules</item>
///   <item>byCategory sums across the whole year (not per month) and orders by plannedMyShare descending</item>
///   <item>totals equal the sum of the 12 months</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class DashboardYearTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 30, 12, 0, 0, TimeSpan.Zero);

    // Mirrors the handler's per-month aggregation over an already-fetched year of entries.
    private static List<(int Month, decimal PlannedExpense, decimal ActualExpense, decimal PlannedIncome, decimal ActualIncome, decimal SaldoPrevisto, decimal SaldoReal)>
        ComputeMonths(IReadOnlyList<BillEntry> billEntries, IReadOnlyList<IncomeEntry> incomeEntries)
    {
        var billsByMonth = billEntries.ToLookup(e => e.RefMonth);
        var incomesByMonth = incomeEntries.ToLookup(e => e.RefMonth);

        return Enumerable.Range(1, 12)
            .Select(m =>
            {
                var monthBills = billsByMonth[m];
                var monthIncomes = incomesByMonth[m];

                var plannedExpense = monthBills.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
                var actualExpense = monthBills
                    .Where(e => e.Paid)
                    .Sum(e => EntryCalculations.MyShare(
                        EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                        e.SplitRatioSnapshot));

                var plannedIncome = monthIncomes.Sum(e => e.PlannedAmount);
                var actualIncome = monthIncomes
                    .Where(e => e.Received)
                    .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

                return (m, plannedExpense, actualExpense, plannedIncome, actualIncome,
                    plannedIncome - plannedExpense, actualIncome - actualExpense);
            })
            .ToList();
    }

    // Mirrors the handler's whole-year per-category aggregation.
    private static List<(long CategoryId, decimal PlannedMyShare, decimal ActualMyShare)> ComputeByCategory(
        IReadOnlyDictionary<long, Bill> billsById, IReadOnlyList<BillEntry> billEntries)
    {
        return billEntries
            .GroupBy(e => billsById[e.BillId].CategoryId)
            .Select(g =>
            {
                var plannedMyShare = g.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
                var actualMyShare = g
                    .Where(e => e.Paid)
                    .Sum(e => EntryCalculations.MyShare(
                        EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                        e.SplitRatioSnapshot));
                return (g.Key, plannedMyShare, actualMyShare);
            })
            .OrderByDescending(r => r.plannedMyShare)
            .ToList();
    }

    // --- 12-position series ---

    [Test]
    public void Months_EmptyYear_AlwaysReturnsTwelvePositionsZeroed()
    {
        // Arrange / Act
        var months = ComputeMonths([], []);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(months, Has.Count.EqualTo(12));
            Assert.That(months.Select(m => m.Month), Is.EqualTo(Enumerable.Range(1, 12)));
            Assert.That(months.All(m => m.PlannedExpense == 0m && m.ActualExpense == 0m
                && m.PlannedIncome == 0m && m.ActualIncome == 0m
                && m.SaldoPrevisto == 0m && m.SaldoReal == 0m), Is.True);
        });
    }

    [Test]
    public void Months_OnlyOneMonthHasData_OtherElevenStayZeroed()
    {
        // Arrange — a single bill entry in March; every other month has no data
        var entry = BillEntry.Create(1L, 10L, 2026, 3, 1000m, 1m, null, FixedNow);
        var months = ComputeMonths([entry], []);

        // Act / Assert
        Assert.Multiple(() =>
        {
            Assert.That(months[2].Month, Is.EqualTo(3));
            Assert.That(months[2].PlannedExpense, Is.EqualTo(1000m));
            Assert.That(months.Where((_, i) => i != 2).All(m => m.PlannedExpense == 0m), Is.True);
        });
    }

    // --- Per-month formulas ---

    [Test]
    public void PlannedExpense_SumsAllEntriesRegardlessOfPaidStatus()
    {
        // Arrange — two entries in January, only one paid
        var paid = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        paid.MarkPaid(FixedNow);
        var unpaid = BillEntry.Create(1L, 10L, 2026, 1, 2000m, 0.5m, 99L, FixedNow);

        // Act
        var months = ComputeMonths([paid, unpaid], []);

        // Assert — (1000 x 0.5) + (2000 x 0.5) = 1500
        Assert.That(months[0].PlannedExpense, Is.EqualTo(1500m));
    }

    [Test]
    public void ActualExpense_SumsOnlyPaidEntries()
    {
        // Arrange
        var paid = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        paid.MarkPaid(FixedNow, actualAmount: 900m);
        var unpaid = BillEntry.Create(1L, 10L, 2026, 1, 2000m, 0.5m, 99L, FixedNow);

        // Act
        var months = ComputeMonths([paid, unpaid], []);

        // Assert — only the paid entry counts: 900 x 0.5 = 450
        Assert.That(months[0].ActualExpense, Is.EqualTo(450m));
    }

    [Test]
    public void PlannedIncome_IncludesAllEntriesRegardlessOfReceivedStatus()
    {
        // Arrange
        var received = IncomeEntry.Create(1L, 30L, 2026, 1, 3000m, FixedNow);
        received.MarkReceived(FixedNow);
        var notReceived = IncomeEntry.Create(1L, 31L, 2026, 1, 2000m, FixedNow);

        // Act
        var months = ComputeMonths([], [received, notReceived]);

        // Assert
        Assert.That(months[0].PlannedIncome, Is.EqualTo(5000m));
    }

    [Test]
    public void ActualIncome_IncludesOnlyReceivedEntries()
    {
        // Arrange
        var received = IncomeEntry.Create(1L, 30L, 2026, 1, 3000m, FixedNow);
        received.MarkReceived(FixedNow, actualAmount: 3200m);
        var notReceived = IncomeEntry.Create(1L, 31L, 2026, 1, 2000m, FixedNow);

        // Act
        var months = ComputeMonths([], [received, notReceived]);

        // Assert
        Assert.That(months[0].ActualIncome, Is.EqualTo(3200m));
    }

    [Test]
    public void SaldoPrevisto_DeductsPlannedExpenseFromPlannedIncome()
    {
        // Arrange
        var income = IncomeEntry.Create(1L, 30L, 2026, 1, 6000m, FixedNow);
        var bill = BillEntry.Create(1L, 10L, 2026, 1, 2000m, 0.5m, 99L, FixedNow); // planned my share = 1000

        // Act
        var months = ComputeMonths([bill], [income]);

        // Assert
        Assert.That(months[0].SaldoPrevisto, Is.EqualTo(5000m));
    }

    [Test]
    public void SaldoReal_DeductsActualExpenseFromActualIncome()
    {
        // Arrange
        var income = IncomeEntry.Create(1L, 30L, 2026, 1, 3000m, FixedNow);
        income.MarkReceived(FixedNow);
        var bill = BillEntry.Create(1L, 10L, 2026, 1, 1200m, 0.5m, 99L, FixedNow);
        bill.MarkPaid(FixedNow); // actual my share = 1200 x 0.5 = 600

        // Act
        var months = ComputeMonths([bill], [income]);

        // Assert
        Assert.That(months[0].SaldoReal, Is.EqualTo(2400m));
    }

    // --- byCategory: whole-year aggregation and ordering ---

    [Test]
    public void ByCategory_SumsAcrossWholeYearNotPerMonth()
    {
        // Arrange — same bill/category, one entry in January and one in July
        var bill = Bill.Create(1L, "Internet", 1L, BillKind.Recurring, 100m, 1m, null, FixedNow);
        var billsById = new Dictionary<long, Bill> { [10L] = bill };
        var entries = new List<BillEntry>
        {
            BillEntry.Create(1L, 10L, 2026, 1, 100m, 1m, null, FixedNow),
            BillEntry.Create(1L, 10L, 2026, 7, 150m, 1m, null, FixedNow),
        };

        // Act
        var byCategory = ComputeByCategory(billsById, entries);

        // Assert — 100 + 150 = 250, summed regardless of month
        Assert.Multiple(() =>
        {
            Assert.That(byCategory, Has.Count.EqualTo(1));
            Assert.That(byCategory[0].PlannedMyShare, Is.EqualTo(250m));
        });
    }

    [Test]
    public void ByCategory_OrdersByPlannedMyShareDescending()
    {
        // Arrange — category 1 (bill A, planned 300) and category 2 (bill B, planned 1000)
        var billA = Bill.Create(1L, "Internet", 1L, BillKind.Recurring, 300m, 1m, null, FixedNow);
        var billB = Bill.Create(1L, "Aluguel", 2L, BillKind.Recurring, 1000m, 1m, null, FixedNow);
        var billsById = new Dictionary<long, Bill> { [10L] = billA, [20L] = billB };
        var entries = new List<BillEntry>
        {
            BillEntry.Create(1L, 10L, 2026, 1, 300m, 1m, null, FixedNow),
            BillEntry.Create(1L, 20L, 2026, 1, 1000m, 1m, null, FixedNow),
        };

        // Act
        var byCategory = ComputeByCategory(billsById, entries);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(byCategory[0].CategoryId, Is.EqualTo(2L));
            Assert.That(byCategory[1].CategoryId, Is.EqualTo(1L));
        });
    }

    [Test]
    public void ByCategory_EmptyYear_ReturnsEmptyList()
    {
        // Arrange / Act
        var byCategory = ComputeByCategory(new Dictionary<long, Bill>(), []);

        // Assert
        Assert.That(byCategory, Is.Empty);
    }

    // --- Totals equal sum of the 12 months ---

    [Test]
    public void Totals_EqualSumOfTwelveMonths()
    {
        // Arrange — entries spread across two distinct months
        var billJan = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 1m, null, FixedNow);
        billJan.MarkPaid(FixedNow, actualAmount: 900m);
        var billJul = BillEntry.Create(1L, 10L, 2026, 7, 500m, 1m, null, FixedNow);
        var incomeJan = IncomeEntry.Create(1L, 30L, 2026, 1, 3000m, FixedNow);
        incomeJan.MarkReceived(FixedNow);

        // Act
        var months = ComputeMonths([billJan, billJul], [incomeJan]);
        var totals = (
            PlannedExpense: months.Sum(m => m.PlannedExpense),
            ActualExpense: months.Sum(m => m.ActualExpense),
            PlannedIncome: months.Sum(m => m.PlannedIncome),
            ActualIncome: months.Sum(m => m.ActualIncome),
            SaldoPrevisto: months.Sum(m => m.SaldoPrevisto),
            SaldoReal: months.Sum(m => m.SaldoReal));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(totals.PlannedExpense, Is.EqualTo(1500m)); // 1000 + 500
            Assert.That(totals.ActualExpense, Is.EqualTo(900m));
            Assert.That(totals.PlannedIncome, Is.EqualTo(3000m));
            Assert.That(totals.ActualIncome, Is.EqualTo(3000m));
            Assert.That(totals.SaldoPrevisto, Is.EqualTo(totals.PlannedIncome - totals.PlannedExpense));
            Assert.That(totals.SaldoReal, Is.EqualTo(totals.ActualIncome - totals.ActualExpense));
        });
    }
}
