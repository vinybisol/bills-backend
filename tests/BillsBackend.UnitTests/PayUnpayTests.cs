using Domain.Entities;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for BillEntry and IncomeEntry pay/unpay/freeze/edit domain logic.
/// </summary>
[TestFixture]
public sealed class PayUnpayTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static BillEntry MakeBillEntry(decimal planned = 1000m) =>
        BillEntry.Create(1L, 9L, 2026, 7, planned, 1m, null, Now.AddDays(-5));

    private static IncomeEntry MakeIncomeEntry(decimal planned = 5000m) =>
        IncomeEntry.Create(1L, 3L, 2026, 7, planned, Now.AddDays(-5));

    // --- BillEntry.MarkPaid ---

    [Test]
    public void MarkPaid_WithoutActualAmount_UsesPlannedAmountAsActual()
    {
        var entry = MakeBillEntry(1000m);

        entry.MarkPaid(Now);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Paid, Is.True);
            Assert.That(entry.ActualAmount, Is.EqualTo(1000m));
            Assert.That(entry.PaidDate, Is.EqualTo(Now));
        });
    }

    [Test]
    public void MarkPaid_WithActualAmount_RecordsIt()
    {
        var entry = MakeBillEntry(1000m);

        entry.MarkPaid(Now, actualAmount: 980m);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Paid, Is.True);
            Assert.That(entry.ActualAmount, Is.EqualTo(980m));
        });
    }

    // --- BillEntry.Unfreeze ---

    [Test]
    public void Unfreeze_ClearsPaidAndPaidDate()
    {
        var entry = MakeBillEntry();
        entry.MarkPaid(Now);
        Assert.That(entry.Paid, Is.True);

        entry.Unfreeze();

        Assert.Multiple(() =>
        {
            Assert.That(entry.Paid, Is.False);
            Assert.That(entry.PaidDate, Is.Null);
        });
    }

    // --- BillEntry.UpdateAmounts ---

    [Test]
    public void UpdateAmounts_UnfrozenEntry_UpdatesBothFields()
    {
        var entry = MakeBillEntry(1000m);

        entry.UpdateAmounts(plannedAmount: 1100m, actualAmount: 1090m);

        Assert.Multiple(() =>
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(1100m));
            Assert.That(entry.ActualAmount, Is.EqualTo(1090m));
        });
    }

    [Test]
    public void UpdateAmounts_OnlyPlanned_DoesNotTouchActual()
    {
        var entry = MakeBillEntry(1000m);

        entry.UpdateAmounts(plannedAmount: 1200m, actualAmount: null);

        Assert.Multiple(() =>
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(1200m));
            Assert.That(entry.ActualAmount, Is.Null);
        });
    }

    [Test]
    public void UpdateAmounts_NegativePlanned_Throws()
    {
        var entry = MakeBillEntry();

        Assert.That(
            () => entry.UpdateAmounts(plannedAmount: -1m, actualAmount: null),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // --- IncomeEntry.MarkReceived ---

    [Test]
    public void IncomeMarkReceived_WithoutActualAmount_UsesPlanned()
    {
        var entry = MakeIncomeEntry(5000m);

        entry.MarkReceived(Now);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Received, Is.True);
            Assert.That(entry.ActualAmount, Is.EqualTo(5000m));
            Assert.That(entry.ReceivedDate, Is.EqualTo(Now));
        });
    }

    [Test]
    public void IncomeMarkReceived_WithActualAmount_RecordsIt()
    {
        var entry = MakeIncomeEntry(5000m);

        entry.MarkReceived(Now, actualAmount: 5200m);

        Assert.That(entry.ActualAmount, Is.EqualTo(5200m));
    }

    // --- IncomeEntry.Unfreeze ---

    [Test]
    public void IncomeUnfreeze_ClearsReceivedAndReceivedDate()
    {
        var entry = MakeIncomeEntry();
        entry.MarkReceived(Now);
        Assert.That(entry.Received, Is.True);

        entry.Unfreeze();

        Assert.Multiple(() =>
        {
            Assert.That(entry.Received, Is.False);
            Assert.That(entry.ReceivedDate, Is.Null);
        });
    }

    // --- IncomeEntry.UpdateAmounts ---

    [Test]
    public void IncomeUpdateAmounts_UpdatesBothFields()
    {
        var entry = MakeIncomeEntry(5000m);

        entry.UpdateAmounts(plannedAmount: 5500m, actualAmount: 5300m);

        Assert.Multiple(() =>
        {
            Assert.That(entry.PlannedAmount, Is.EqualTo(5500m));
            Assert.That(entry.ActualAmount, Is.EqualTo(5300m));
        });
    }
}
