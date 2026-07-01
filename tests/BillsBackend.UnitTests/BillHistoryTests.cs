using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the bill-history filtering/ordering/variation/aggregation logic used by
/// <c>GET /api/bills/{billId}/history</c>.
/// <para>
/// This codebase does not extract handler bodies into separately-testable classes, so these
/// tests construct real <see cref="BillEntry"/> objects and run the same filter/order/variation/sum
/// logic the endpoint performs, asserting on the result.
/// </para>
/// </summary>
[TestFixture]
public sealed class BillHistoryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 07, 05, 12, 0, 0, TimeSpan.Zero);

    private sealed record Item(
        int Year, int Month, decimal Effective, decimal MyShare, bool Paid, EntryCalculations.Variation? Variation);

    // Mirrors the handler's optional from/to period filter (IsInForwardRange applied both ways),
    // ascending chronological ordering, per-item variation vs. the previous item, and aggregation.
    private static (List<Item> Items, decimal Avg, decimal Min, decimal Max, decimal TotalPaidMyShare) ComputeHistory(
        IReadOnlyList<BillEntry> entries,
        int? fromYear = null, int? fromMonth = null,
        int? toYear = null, int? toMonth = null)
    {
        IEnumerable<BillEntry> filtered = entries;

        if (fromYear.HasValue && fromMonth.HasValue)
        {
            filtered = filtered.Where(e =>
                EntryCalculations.IsInForwardRange(e.RefYear, e.RefMonth, fromYear.Value, fromMonth.Value));
        }

        if (toYear.HasValue && toMonth.HasValue)
        {
            filtered = filtered.Where(e =>
                EntryCalculations.IsInForwardRange(toYear.Value, toMonth.Value, e.RefYear, e.RefMonth));
        }

        var ordered = filtered.OrderBy(e => e.RefYear).ThenBy(e => e.RefMonth).ToList();

        var items = new List<Item>(ordered.Count);
        decimal? previousEffective = null;
        foreach (var e in ordered)
        {
            var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
            var myShare = EntryCalculations.MyShare(effective, e.SplitRatioSnapshot);
            var variation = EntryCalculations.ComputeVariation(effective, previousEffective);
            items.Add(new Item(e.RefYear, e.RefMonth, effective, myShare, e.Paid, variation));
            previousEffective = effective;
        }

        var avg = items.Count > 0 ? items.Average(i => i.Effective) : 0m;
        var min = items.Count > 0 ? items.Min(i => i.Effective) : 0m;
        var max = items.Count > 0 ? items.Max(i => i.Effective) : 0m;
        var totalPaidMyShare = items.Where(i => i.Paid).Sum(i => i.MyShare);

        return (items, avg, min, max, totalPaidMyShare);
    }

    // --- Chronological ordering ---

    [Test]
    public void DefaultOrdering_IsYearThenMonthAscending()
    {
        // Arrange — out-of-order input across two years
        var jan2026 = BillEntry.Create(1L, 10L, 2026, 1, 100m, 1m, null, FixedNow);
        var dec2025 = BillEntry.Create(1L, 10L, 2025, 12, 100m, 1m, null, FixedNow);
        var jun2026 = BillEntry.Create(1L, 10L, 2026, 6, 100m, 1m, null, FixedNow);

        // Act
        var (items, _, _, _, _) = ComputeHistory([jan2026, dec2025, jun2026]);

        // Assert — oldest first
        Assert.Multiple(() =>
        {
            Assert.That(items[0], Is.EqualTo(new Item(2025, 12, 100m, 100m, false, null)));
            Assert.That(items[1].Month, Is.EqualTo(1));
            Assert.That(items[2].Month, Is.EqualTo(6));
        });
    }

    // --- Variation ---

    [Test]
    public void Variation_FirstItem_IsNull()
    {
        // Arrange
        var only = BillEntry.Create(1L, 10L, 2026, 1, 150m, 1m, null, FixedNow);

        // Act
        var (items, _, _, _, _) = ComputeHistory([only]);

        // Assert
        Assert.That(items[0].Variation, Is.Null);
    }

    [Test]
    public void Variation_SubsequentItems_ComparedToPreviousInSeries()
    {
        // Arrange — 150 -> 152 (+2) -> 100 (-52)
        var first = BillEntry.Create(1L, 10L, 2026, 1, 150m, 1m, null, FixedNow);
        var second = BillEntry.Create(1L, 10L, 2026, 2, 150m, 1m, null, FixedNow);
        second.UpdateAmounts(plannedAmount: null, actualAmount: 152m);
        var third = BillEntry.Create(1L, 10L, 2026, 3, 100m, 1m, null, FixedNow);

        // Act
        var (items, _, _, _, _) = ComputeHistory([first, second, third]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items[0].Variation, Is.Null);
            Assert.That(items[1].Variation!.Value.Abs, Is.EqualTo(2m));
            Assert.That(items[1].Variation!.Value.Pct, Is.EqualTo(1.33m));
            Assert.That(items[2].Variation!.Value.Abs, Is.EqualTo(-52m));
        });
    }

    // --- Aggregates ---

    [Test]
    public void Aggregates_AvgMinMaxAndTotalPaidMyShare_AreCorrect()
    {
        // Arrange — three entries, only the first two paid
        var a = BillEntry.Create(1L, 10L, 2026, 1, 100m, 0.5m, 99L, FixedNow);
        a.MarkPaid(FixedNow);
        var b = BillEntry.Create(1L, 10L, 2026, 2, 200m, 0.5m, 99L, FixedNow);
        b.MarkPaid(FixedNow);
        var c = BillEntry.Create(1L, 10L, 2026, 3, 300m, 0.5m, 99L, FixedNow);

        // Act
        var (_, avg, min, max, totalPaidMyShare) = ComputeHistory([a, b, c]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(avg, Is.EqualTo(200m)); // (100+200+300)/3
            Assert.That(min, Is.EqualTo(100m));
            Assert.That(max, Is.EqualTo(300m));
            Assert.That(totalPaidMyShare, Is.EqualTo(150m)); // 50 (a) + 100 (b), c unpaid
        });
    }

    [Test]
    public void Aggregates_NoItems_AreZero()
    {
        // Arrange / Act — an empty slice must not throw (Average/Min/Max on empty sequences do)
        var (items, avg, min, max, totalPaidMyShare) = ComputeHistory([]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items, Is.Empty);
            Assert.That(avg, Is.EqualTo(0m));
            Assert.That(min, Is.EqualTo(0m));
            Assert.That(max, Is.EqualTo(0m));
            Assert.That(totalPaidMyShare, Is.EqualTo(0m));
        });
    }

    // --- MyShare across split ratios ---

    [TestCase(1.0, 200, 200)]
    [TestCase(0.5, 200, 100)]
    [TestCase(0.0, 200, 0)]
    public void MyShare_AcrossSplitRatios_IsCorrect(decimal splitRatio, decimal effective, decimal expectedMyShare)
    {
        // Arrange
        var entry = BillEntry.Create(1L, 10L, 2026, 1, effective, splitRatio, splitRatio < 1m ? 99L : null, FixedNow);

        // Act
        var (items, _, _, _, _) = ComputeHistory([entry]);

        // Assert
        Assert.That(items[0].MyShare, Is.EqualTo(expectedMyShare));
    }

    // --- Period range filtering ---

    [Test]
    public void PeriodFilter_FromAndToWindow_ExcludesOutsideEntries()
    {
        // Arrange — entries in Feb (before), May (inside), Sep (after) a Mar-Jun window
        var before = BillEntry.Create(1L, 10L, 2026, 2, 100m, 1m, null, FixedNow);
        var inside = BillEntry.Create(1L, 10L, 2026, 5, 200m, 1m, null, FixedNow);
        var after = BillEntry.Create(1L, 10L, 2026, 9, 300m, 1m, null, FixedNow);

        // Act
        var (items, _, _, _, _) = ComputeHistory(
            [before, inside, after], fromYear: 2026, fromMonth: 3, toYear: 2026, toMonth: 6);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Month, Is.EqualTo(5));
        });
    }
}
