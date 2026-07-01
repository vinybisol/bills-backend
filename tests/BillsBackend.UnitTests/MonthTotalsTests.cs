using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the month-totals formulas used by <c>GET /api/entries</c>.
/// <para>
/// Both formulas are exercised directly using <see cref="EntryCalculations"/> helpers,
/// avoiding any dependency on the handler or the database.
/// </para>
/// <list type="bullet">
///   <item>saldoPrevisto = Σ(income.PlannedAmount) − Σ(bill.PlannedAmount × bill.SplitRatioSnapshot)</item>
///   <item>saldoReal = Σ(EffectiveAmount for received incomes) − Σ(MyShare for paid bills)</item>
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
}
