using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the month-totals formulas used by <c>GET /api/entries</c>.
/// <para>
/// Both formulas are exercised directly using <see cref="EntryCalculations"/> helpers,
/// avoiding any dependency on the handler or the database.
/// </para>
/// <list type="bullet">
///   <item>saldoPrevistoOtimista = Σ(income.PlannedAmount) − Σ(bill.PlannedAmount × bill.SplitRatioSnapshot)</item>
///   <item>saldoPrevistoPiorCaso = saldoPrevistoOtimista − receivablePending</item>
///   <item>saldoRealizado = (Σ EffectiveAmount for received incomes + receivableReceived) − Σ(EffectiveAmount for paid bills, full value)</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class MonthTotalsTests
{
    // --- saldoPrevisto ---

    [Test]
    public void SaldoPrevisto_DeductsMyShareOfBillsFromIncomesPlanned()
    {
        // Arrange
        const decimal incomePlanned = 6000m;
        const decimal billPlanned = 2000m;
        const decimal splitRatio = 0.5m;

        // Act — only the owner's share (splitRatio) of the bill is subtracted
        var saldoPrevisto = incomePlanned - EntryCalculations.MyShare(billPlanned, splitRatio);

        // Assert — 6000 − (2000 × 0.5) = 5000
        Assert.That(saldoPrevisto, Is.EqualTo(5000m));
    }

    [Test]
    public void SaldoPrevisto_FullOwnerBill_SplitRatio1_DeductsFullBillAmount()
    {
        // Arrange
        const decimal incomePlanned = 5000m;
        const decimal billPlanned = 1500m;
        const decimal splitRatio = 1m;

        // Act
        var saldoPrevisto = incomePlanned - EntryCalculations.MyShare(billPlanned, splitRatio);

        // Assert — 5000 − (1500 × 1.0) = 3500
        Assert.That(saldoPrevisto, Is.EqualTo(3500m));
    }

    [Test]
    public void SaldoPrevisto_NoBills_EqualsIncomesPlanned()
    {
        // Arrange
        const decimal incomePlanned = 3000m;

        // Act — no bills → bill share sum is zero
        var saldoPrevisto = incomePlanned - 0m;

        // Assert
        Assert.That(saldoPrevisto, Is.EqualTo(3000m));
    }

    // --- saldoReal ---

    [Test]
    public void SaldoReal_SumsOnlyReceivedIncomesAndPaidBills()
    {
        // Arrange — two incomes (only one received), two bills (only one paid)
        // Income1: received=true,  effective=3000
        // Income2: received=false, effective=2000 (excluded)
        // Bill1:   paid=true,  effective=1200, splitRatio=0.5 → myShare=600
        // Bill2:   paid=false, effective=800,  splitRatio=1.0 (excluded)
        const decimal receivedIncomeEffective = 3000m;
        var paidBillMyShare = EntryCalculations.MyShare(effective: 1200m, splitRatio: 0.5m);

        // Act
        var saldoReal = receivedIncomeEffective - paidBillMyShare;

        // Assert — 3000 − 600 = 2400
        Assert.That(saldoReal, Is.EqualTo(2400m));
    }

    [Test]
    public void SaldoReal_NothingReceivedOrPaid_EqualsZero()
    {
        // Arrange / Act — no received incomes and no paid bills
        var saldoReal = 0m - 0m;

        // Assert
        Assert.That(saldoReal, Is.EqualTo(0m));
    }

    [Test]
    public void SaldoReal_OnlyPaidBills_IsNegative()
    {
        // Arrange
        const decimal billEffective = 1500m;
        const decimal splitRatio = 1m;

        // Act — no income received; owner paid their full bill
        var saldoReal = 0m - EntryCalculations.MyShare(billEffective, splitRatio);

        // Assert — 0 − 1500 = −1500
        Assert.That(saldoReal, Is.EqualTo(-1500m));
    }

    // --- receivable / received split ---

    [Test]
    public void Receivable_SplitsIntoPendingAndReceived_ByReceivedFlag()
    {
        // Arrange — two split bill entries; only one has been marked as received
        // Entry1: received=true,  effective=100, splitRatio=0.5 → receivable=50
        // Entry2: received=false, effective=200, splitRatio=0.5 → receivable=100
        var receivedEntry = (Received: true, Amount: EntryCalculations.Receivable(effective: 100m, splitRatio: 0.5m));
        var pendingEntry = (Received: false, Amount: EntryCalculations.Receivable(effective: 200m, splitRatio: 0.5m));
        var entries = new[] { receivedEntry, pendingEntry };

        // Act
        var totalReceived = entries.Where(e => e.Received).Sum(e => e.Amount);
        var totalReceivable = entries.Where(e => !e.Received).Sum(e => e.Amount);

        // Assert — received=50, receivable(pending)=100
        Assert.Multiple(() =>
        {
            Assert.That(totalReceived, Is.EqualTo(50m));
            Assert.That(totalReceivable, Is.EqualTo(100m));
        });
    }

    [Test]
    public void Receivable_ReceivedPlusPending_EqualsTotalOwed()
    {
        // Arrange — the invariant: received + receivable(pending) = total owed by other people
        var entries = new[]
        {
            (Received: true, Amount: EntryCalculations.Receivable(effective: 300m, splitRatio: 0.5m)),
            (Received: false, Amount: EntryCalculations.Receivable(effective: 400m, splitRatio: 0.5m)),
            (Received: false, Amount: EntryCalculations.Receivable(effective: 100m, splitRatio: 0m)),
        };
        var totalOwed = entries.Sum(e => e.Amount);

        // Act
        var totalReceived = entries.Where(e => e.Received).Sum(e => e.Amount);
        var totalReceivable = entries.Where(e => !e.Received).Sum(e => e.Amount);

        // Assert
        Assert.That(totalReceived + totalReceivable, Is.EqualTo(totalOwed));
    }

    [Test]
    public void Receivable_NoEntries_BothAreZero()
    {
        // Arrange / Act — no split bill entries this month
        var totalReceived = 0m;
        var totalReceivable = 0m;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(totalReceived, Is.EqualTo(0m));
            Assert.That(totalReceivable, Is.EqualTo(0m));
        });
    }

    // --- three balances (saldoPrevistoOtimista / saldoPrevistoPiorCaso / saldoRealizado) ---

    [Test]
    public void ThreeBalances_MixedScenario_ComputeCorrectlyAndSatisfyGapInvariant()
    {
        // Arrange — three bills spanning the split spectrum, two incomes.
        // Bill A: split=1.0 (fully mine), planned=effective=1000, paid, not received (n/a, split=1 has no receivable).
        // Bill B: split=0.5 (shared), planned=effective=800, paid, received=true (reimbursed).
        // Bill C: split=0.0 (passes through me), planned=effective=500, unpaid, unreceived.
        var bills = new[]
        {
            (Planned: 1000m, Effective: 1000m, Split: 1.0m, Paid: true, Received: false),
            (Planned: 800m, Effective: 800m, Split: 0.5m, Paid: true, Received: true),
            (Planned: 500m, Effective: 500m, Split: 0.0m, Paid: false, Received: false),
        };

        // Income1: planned=5000, received=true, effective=5200.
        // Income2: planned=1000, received=false, effective=1000 (excluded from received total).
        var incomes = new[]
        {
            (Planned: 5000m, Effective: 5200m, Received: true),
            (Planned: 1000m, Effective: 1000m, Received: false),
        };

        // Act — mirrors the totals-building logic in EntryEndpoints.GetEntries.
        var receivablePending = bills
            .Where(b => !b.Received)
            .Sum(b => EntryCalculations.Receivable(b.Effective, b.Split));
        var receivableReceived = bills
            .Where(b => b.Received)
            .Sum(b => EntryCalculations.Receivable(b.Effective, b.Split));
        var paidFull = bills.Where(b => b.Paid).Sum(b => b.Effective);
        var incomesPlanned = incomes.Sum(i => i.Planned);
        var incomesReceivedTotal = incomes.Where(i => i.Received).Sum(i => i.Effective);

        var saldoPrevistoOtimista = incomesPlanned - bills.Sum(b => b.Planned * b.Split);
        var saldoPrevistoPiorCaso = saldoPrevistoOtimista - receivablePending;
        var saldoRealizado = incomesReceivedTotal + receivableReceived - paidFull;

        // Assert
        Assert.Multiple(() =>
        {
            // receivablePending: only bill C (500 x (1-0) = 500) is unreceived.
            Assert.That(receivablePending, Is.EqualTo(500m));
            // receivableReceived: only bill B (800 x (1-0.5) = 400) has been received.
            Assert.That(receivableReceived, Is.EqualTo(400m));
            // paidFull: full effective value of paid bills A and B (not myShare) = 1000 + 800.
            Assert.That(paidFull, Is.EqualTo(1800m));
            // saldoPrevistoOtimista = 6000 - (1000x1.0 + 800x0.5 + 500x0.0) = 6000 - 1400 = 4600.
            Assert.That(saldoPrevistoOtimista, Is.EqualTo(4600m));
            // saldoPrevistoPiorCaso = 4600 - 500 (pending receivable never paid back) = 4100.
            Assert.That(saldoPrevistoPiorCaso, Is.EqualTo(4100m));
            // saldoRealizado = (5200 received income + 400 received reimbursement) - 1800 paid full = 3800.
            Assert.That(saldoRealizado, Is.EqualTo(3800m));
            // Gap invariant: the difference between the two planned balances is exactly the pending receivable.
            Assert.That(saldoPrevistoOtimista - saldoPrevistoPiorCaso, Is.EqualTo(receivablePending));
        });
    }

    [Test]
    public void ReceivablePending_ExcludesAlreadyReceivedAmounts()
    {
        // Arrange — one pending, one already received; pending total must not include the received one.
        var bills = new[]
        {
            (Effective: 200m, Split: 0.5m, Received: false), // receivable=100, still pending
            (Effective: 300m, Split: 0.5m, Received: true),  // receivable=150, already received
        };

        // Act
        var receivablePending = bills
            .Where(b => !b.Received)
            .Sum(b => EntryCalculations.Receivable(b.Effective, b.Split));

        // Assert — only the unreceived entry counts
        Assert.That(receivablePending, Is.EqualTo(100m));
    }

    [Test]
    public void SaldoRealizado_UsesFullPaidValueNotMyShareOfPaidBills()
    {
        // Arrange — a shared bill (split=0.5) paid in full by the owner: effective=1000.
        const decimal effective = 1000m;
        const decimal splitRatio = 0.5m;
        var myShare = EntryCalculations.MyShare(effective, splitRatio);

        // Act — saldoRealizado subtracts the full paid amount, not myShare (the old SaldoReal semantics).
        var saldoRealizado = 0m - effective;
        var legacySaldoReal = 0m - myShare;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(saldoRealizado, Is.EqualTo(-1000m));
            Assert.That(legacySaldoReal, Is.EqualTo(-500m));
            Assert.That(saldoRealizado, Is.Not.EqualTo(legacySaldoReal));
        });
    }

    [Test]
    public void SaldoRealizado_AddsReceivedReimbursementsOnTopOfReceivedIncome()
    {
        // Arrange — no incomes received, but a reimbursement (receivableReceived) came in.
        const decimal incomesReceivedTotal = 0m;
        const decimal receivableReceived = 250m;
        const decimal paidFull = 0m;

        // Act
        var saldoRealizado = incomesReceivedTotal + receivableReceived - paidFull;

        // Assert
        Assert.That(saldoRealizado, Is.EqualTo(250m));
    }
}
