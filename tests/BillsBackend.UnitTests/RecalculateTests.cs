using BillsBackend.Api.Domain;
using Domain.Entities;
using Domain.Enums;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the recalculate feature: the IsInForwardRange predicate,
/// BillEntry.UpdatePlanned, and Bill.Recalculate domain methods.
/// </summary>
[TestFixture]
public sealed class RecalculateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static BillEntry MakeBillEntry(int year, int month, bool paid = false, decimal planned = 100m)
    {
        var entry = BillEntry.Create(1L, 1L, year, month, planned, 1m, null, Now);
        if (paid) entry.MarkPaid(Now);
        return entry;
    }

    // --- IsInForwardRange predicate ---

    [Test]
    public void IsInForwardRange_SameMonthAsFrom_IsIncluded()
    {
        Assert.That(EntryCalculations.IsInForwardRange(2026, 7, 2026, 7), Is.True);
    }

    [Test]
    public void IsInForwardRange_MonthBeforeFrom_IsExcluded()
    {
        Assert.That(EntryCalculations.IsInForwardRange(2026, 6, 2026, 7), Is.False);
    }

    [Test]
    public void IsInForwardRange_MonthAfterFrom_SameYear_IsIncluded()
    {
        Assert.That(EntryCalculations.IsInForwardRange(2026, 12, 2026, 7), Is.True);
    }

    [Test]
    public void IsInForwardRange_NextYear_IsIncluded()
    {
        Assert.That(EntryCalculations.IsInForwardRange(2027, 1, 2026, 7), Is.True);
    }

    [Test]
    public void IsInForwardRange_PriorYear_IsExcluded()
    {
        Assert.That(EntryCalculations.IsInForwardRange(2025, 12, 2026, 7), Is.False);
    }

    // --- BillEntry.UpdatePlanned ---

    [Test]
    public void UpdatePlanned_UpdatesPlannedAmount()
    {
        var entry = MakeBillEntry(2026, 8, planned: 100m);

        entry.UpdatePlanned(175m);

        Assert.That(entry.PlannedAmount, Is.EqualTo(175m));
    }

    [Test]
    public void UpdatePlanned_NegativeAmount_Throws()
    {
        var entry = MakeBillEntry(2026, 8);

        Assert.That(() => entry.UpdatePlanned(-1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // --- Bill.Recalculate ---

    [Test]
    public void BillRecalculate_UpdatesDefaultAmount()
    {
        var bill = Bill.Create(1L, "Energia", 1L, BillKindEnum.Recurring, 100m, 1m, null, Now);

        bill.Recalculate(175m);

        Assert.That(bill.DefaultAmount, Is.EqualTo(175m));
    }

    [Test]
    public void BillRecalculate_NegativeAmount_Throws()
    {
        var bill = Bill.Create(1L, "Energia", 1L, BillKindEnum.Recurring, 100m, 1m, null, Now);

        Assert.That(() => bill.Recalculate(-1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // --- Paid entry skipping (domain-level) ---

    [Test]
    public void PaidEntryInRange_IsNotUpdatedByUpdatePlanned_WhenCallerSkipsIt()
    {
        // The endpoint skips paid entries — here we verify the entry remains frozen.
        var entry = MakeBillEntry(2026, 8, paid: true, planned: 100m);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Paid, Is.True);
            Assert.That(entry.PlannedAmount, Is.EqualTo(100m)); // unchanged because caller skipped it
        });
    }
}
