using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the dashboard-month aggregation logic used by <c>GET /api/dashboard/month</c>.
/// <para>
/// This codebase does not extract handler bodies into separately-testable classes, so these
/// tests construct real <see cref="BillEntry"/>/<see cref="IncomeEntry"/>/<see cref="Bill"/>/
/// <see cref="Category"/> domain objects and run the same LINQ grouping/filtering the endpoint
/// performs, asserting on the result.
/// </para>
/// <list type="bullet">
///   <item>plannedMyShare = Σ(entry.PlannedAmount × entry.SplitRatioSnapshot) over all entries</item>
///   <item>actualMyShare = Σ(MyShare(EffectiveAmount) over paid entries only</item>
///   <item>diff = actualMyShare − plannedMyShare</item>
///   <item>plannedIncome/actualIncome mirror the planned/received rule for incomes</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class DashboardMonthTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 30, 12, 0, 0, TimeSpan.Zero);

    // Builds a category -> bill -> entries fixture mirroring the handler's grouping key
    // (bill.CategoryId), returning the per-category rows the handler would compute.
    private static List<(long CategoryId, decimal PlannedMyShare, decimal ActualMyShare, decimal Diff)> ComputeByCategory(
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
                return (g.Key, plannedMyShare, actualMyShare, actualMyShare - plannedMyShare);
            })
            .OrderByDescending(r => r.plannedMyShare)
            .ToList();
    }

    // --- Per-category plannedMyShare / actualMyShare ---

    [Test]
    public void PlannedMyShare_SumsAllEntriesRegardlessOfPaidStatus()
    {
        // Arrange — two entries in the same category, only one paid
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 1000m, 0.5m, 99L, FixedNow);
        var billsById = new Dictionary<long, Bill> { [10L] = bill };
        var paid = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        paid.MarkPaid(FixedNow);
        var unpaid = BillEntry.Create(1L, 10L, 2026, 1, 2000m, 0.5m, 99L, FixedNow);
        var entries = new List<BillEntry> { paid, unpaid };

        // Act
        var byCategory = ComputeByCategory(billsById, entries);

        // Assert — (1000 x 0.5) + (2000 x 0.5) = 1500, regardless of paid status
        Assert.That(byCategory[0].PlannedMyShare, Is.EqualTo(1500m));
    }

    [Test]
    public void ActualMyShare_SumsOnlyPaidEntries()
    {
        // Arrange — same fixture as above: only the first entry is paid
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 1000m, 0.5m, 99L, FixedNow);
        var billsById = new Dictionary<long, Bill> { [10L] = bill };
        var paid = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        paid.MarkPaid(FixedNow, actualAmount: 900m);
        var unpaid = BillEntry.Create(1L, 10L, 2026, 1, 2000m, 0.5m, 99L, FixedNow);
        var entries = new List<BillEntry> { paid, unpaid };

        // Act
        var byCategory = ComputeByCategory(billsById, entries);

        // Assert — only paid entry counts: 900 x 0.5 = 450
        Assert.That(byCategory[0].ActualMyShare, Is.EqualTo(450m));
    }

    [Test]
    public void Diff_OverspendYieldsPositiveValue()
    {
        // Arrange — planned 500 x 1.0 = 500; actual (paid) 700 x 1.0 = 700 → diff = +200
        var bill = Bill.Create(1L, "Mercado", 1L, BillKind.Recurring, 500m, 1m, null, FixedNow);
        var billsById = new Dictionary<long, Bill> { [10L] = bill };
        var entry = BillEntry.Create(1L, 10L, 2026, 1, 500m, 1m, null, FixedNow);
        entry.MarkPaid(FixedNow, actualAmount: 700m);
        var entries = new List<BillEntry> { entry };

        // Act
        var byCategory = ComputeByCategory(billsById, entries);

        // Assert
        Assert.That(byCategory[0].Diff, Is.EqualTo(200m));
    }

    [Test]
    public void Diff_UnderspendYieldsNegativeValue()
    {
        // Arrange — planned 500 x 1.0 = 500; actual (paid) 300 x 1.0 = 300 → diff = -200
        var bill = Bill.Create(1L, "Mercado", 1L, BillKind.Recurring, 500m, 1m, null, FixedNow);
        var billsById = new Dictionary<long, Bill> { [10L] = bill };
        var entry = BillEntry.Create(1L, 10L, 2026, 1, 500m, 1m, null, FixedNow);
        entry.MarkPaid(FixedNow, actualAmount: 300m);
        var entries = new List<BillEntry> { entry };

        // Act
        var byCategory = ComputeByCategory(billsById, entries);

        // Assert
        Assert.That(byCategory[0].Diff, Is.EqualTo(-200m));
    }

    // --- Grouping and ordering ---

    [Test]
    public void ByCategory_GroupsEntriesByCategoryAndOrdersByPlannedMyShareDescending()
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

        // Assert — one row per category, ordered by plannedMyShare descending
        Assert.Multiple(() =>
        {
            Assert.That(byCategory, Has.Count.EqualTo(2));
            Assert.That(byCategory[0].CategoryId, Is.EqualTo(2L));
            Assert.That(byCategory[0].PlannedMyShare, Is.EqualTo(1000m));
            Assert.That(byCategory[1].CategoryId, Is.EqualTo(1L));
            Assert.That(byCategory[1].PlannedMyShare, Is.EqualTo(300m));
        });
    }

    [Test]
    public void ByCategory_MultipleEntriesSameCategory_ProducesSingleSummedRow()
    {
        // Arrange — two bills, same category, two entries
        var billA = Bill.Create(1L, "Internet", 1L, BillKind.Recurring, 100m, 1m, null, FixedNow);
        var billB = Bill.Create(1L, "Agua", 1L, BillKind.Recurring, 50m, 1m, null, FixedNow);
        var billsById = new Dictionary<long, Bill> { [10L] = billA, [20L] = billB };
        var entries = new List<BillEntry>
        {
            BillEntry.Create(1L, 10L, 2026, 1, 100m, 1m, null, FixedNow),
            BillEntry.Create(1L, 20L, 2026, 1, 50m, 1m, null, FixedNow),
        };

        // Act
        var byCategory = ComputeByCategory(billsById, entries);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(byCategory, Has.Count.EqualTo(1));
            Assert.That(byCategory[0].PlannedMyShare, Is.EqualTo(150m));
        });
    }

    // --- saldoPrevisto / saldoReal (mirrors MonthTotalsTests) ---

    [Test]
    public void SaldoPrevisto_DeductsPlannedExpenseFromPlannedIncome()
    {
        // Arrange
        const decimal plannedIncome = 6000m;
        const decimal plannedExpense = 1000m; // e.g. 2000 planned x 0.5 split ratio

        // Act
        var saldoPrevisto = plannedIncome - plannedExpense;

        // Assert
        Assert.That(saldoPrevisto, Is.EqualTo(5000m));
    }

    [Test]
    public void SaldoReal_DeductsActualExpenseFromActualIncome()
    {
        // Arrange
        const decimal actualIncome = 3000m; // only received incomes
        const decimal actualExpense = 600m; // only paid bills' my share

        // Act
        var saldoReal = actualIncome - actualExpense;

        // Assert
        Assert.That(saldoReal, Is.EqualTo(2400m));
    }

    [Test]
    public void SaldoReal_NothingReceivedOrPaid_EqualsZero()
    {
        // Arrange / Act
        var saldoReal = 0m - 0m;

        // Assert
        Assert.That(saldoReal, Is.EqualTo(0m));
    }

    // --- Income planned / actual ---

    [Test]
    public void ActualIncome_IncludesOnlyReceivedEntries()
    {
        // Arrange — one received, one not
        var received = IncomeEntry.Create(1L, 30L, 2026, 1, 3000m, FixedNow);
        received.MarkReceived(FixedNow, actualAmount: 3200m);
        var notReceived = IncomeEntry.Create(1L, 31L, 2026, 1, 2000m, FixedNow);
        var entries = new List<IncomeEntry> { received, notReceived };

        // Act
        var actualIncome = entries
            .Where(e => e.Received)
            .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

        // Assert — only the received entry's effective amount counts
        Assert.That(actualIncome, Is.EqualTo(3200m));
    }

    [Test]
    public void PlannedIncome_IncludesAllEntriesRegardlessOfReceivedStatus()
    {
        // Arrange — one received, one not
        var received = IncomeEntry.Create(1L, 30L, 2026, 1, 3000m, FixedNow);
        received.MarkReceived(FixedNow);
        var notReceived = IncomeEntry.Create(1L, 31L, 2026, 1, 2000m, FixedNow);
        var entries = new List<IncomeEntry> { received, notReceived };

        // Act
        var plannedIncome = entries.Sum(e => e.PlannedAmount);

        // Assert — both entries count regardless of received status
        Assert.That(plannedIncome, Is.EqualTo(5000m));
    }

    // --- three balances (saldoPrevistoOtimista / saldoPrevistoPiorCaso / saldoRealizado) ---

    // Mirrors the inline receivable/paidFull computation added to DashboardEndpoints.GetDashboardMonth
    // (there is no per-entry Receivable DTO in this endpoint, unlike GET /api/entries).
    private static (decimal ReceivablePending, decimal ReceivableReceived, decimal PaidFull) ComputeReceivables(
        IReadOnlyList<BillEntry> billEntries)
    {
        var receivablePending = billEntries
            .Where(e => !e.Received)
            .Sum(e => EntryCalculations.Receivable(
                EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot));
        var receivableReceived = billEntries
            .Where(e => e.Received)
            .Sum(e => EntryCalculations.Receivable(
                EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot));
        var paidFull = billEntries
            .Where(e => e.Paid)
            .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));
        return (receivablePending, receivableReceived, paidFull);
    }

    [Test]
    public void ThreeBalances_MixedScenario_ComputeCorrectlyAndSatisfyGapInvariant()
    {
        // Arrange — three bills spanning the split spectrum, two incomes.
        // Bill A: split=1.0 (fully mine), planned=1000, paid (actual=1000).
        // Bill B: split=0.5 (shared), planned=800, paid (actual=800), received=true (reimbursed).
        // Bill C: split=0.0 (passes through me), planned=500, unpaid, unreceived.
        var billA = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 1.0m, null, FixedNow);
        billA.MarkPaid(FixedNow, actualAmount: 1000m);

        var billB = BillEntry.Create(1L, 20L, 2026, 1, 800m, 0.5m, 99L, FixedNow);
        billB.MarkPaid(FixedNow, actualAmount: 800m);
        billB.MarkReceived(FixedNow);

        var billC = BillEntry.Create(1L, 30L, 2026, 1, 500m, 0.0m, 99L, FixedNow);

        var billEntries = new List<BillEntry> { billA, billB, billC };

        var incomeReceived = IncomeEntry.Create(1L, 40L, 2026, 1, 5000m, FixedNow);
        incomeReceived.MarkReceived(FixedNow, actualAmount: 5200m);
        var incomeNotReceived = IncomeEntry.Create(1L, 41L, 2026, 1, 1000m, FixedNow);
        var incomeEntries = new List<IncomeEntry> { incomeReceived, incomeNotReceived };

        // Act — mirrors DashboardEndpoints.GetDashboardMonth's summary computation.
        var plannedExpense = billEntries.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
        var plannedIncome = incomeEntries.Sum(e => e.PlannedAmount);
        var actualIncome = incomeEntries
            .Where(e => e.Received)
            .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));
        var (receivablePending, receivableReceived, paidFull) = ComputeReceivables(billEntries);

        var saldoPrevistoOtimista = plannedIncome - plannedExpense;
        var saldoPrevistoPiorCaso = saldoPrevistoOtimista - receivablePending;
        var saldoRealizado = actualIncome + receivableReceived - paidFull;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivablePending, Is.EqualTo(500m)); // bill C: 500 x (1-0)
            Assert.That(receivableReceived, Is.EqualTo(400m)); // bill B: 800 x (1-0.5)
            Assert.That(paidFull, Is.EqualTo(1800m)); // full value of paid bills A + B
            Assert.That(saldoPrevistoOtimista, Is.EqualTo(4600m)); // 6000 - (1000 + 400 + 0)
            Assert.That(saldoPrevistoPiorCaso, Is.EqualTo(4100m)); // 4600 - 500
            Assert.That(saldoRealizado, Is.EqualTo(3800m)); // (5200 + 400) - 1800
            Assert.That(saldoPrevistoOtimista - saldoPrevistoPiorCaso, Is.EqualTo(receivablePending));
        });
    }

    [Test]
    public void ReceivablePending_ExcludesAlreadyReceivedBillEntries()
    {
        // Arrange — one pending, one already received
        var pending = BillEntry.Create(1L, 10L, 2026, 1, 200m, 0.5m, 99L, FixedNow); // receivable=100
        var received = BillEntry.Create(1L, 20L, 2026, 1, 300m, 0.5m, 99L, FixedNow); // receivable=150
        received.MarkReceived(FixedNow);
        var billEntries = new List<BillEntry> { pending, received };

        // Act
        var (receivablePending, _, _) = ComputeReceivables(billEntries);

        // Assert — only the unreceived entry counts
        Assert.That(receivablePending, Is.EqualTo(100m));
    }

    [Test]
    public void SaldoRealizado_UsesFullPaidValueNotMyShareOfPaidBills()
    {
        // Arrange — a shared bill (split=0.5) paid in full: effective=1000
        var bill = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        bill.MarkPaid(FixedNow, actualAmount: 1000m);
        var billEntries = new List<BillEntry> { bill };
        var noIncomes = new List<IncomeEntry>();

        // Act
        var (_, receivableReceived, paidFull) = ComputeReceivables(billEntries);
        var actualIncome = noIncomes
            .Where(e => e.Received)
            .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));
        var saldoRealizado = actualIncome + receivableReceived - paidFull;

        // Assert — subtracts the full 1000, not myShare (500); the bill hasn't been reimbursed yet
        Assert.That(saldoRealizado, Is.EqualTo(-1000m));
    }
}
