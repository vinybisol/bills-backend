using BillsBackend.Api.Domain;
using Domain.Entities;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the receivables-history filtering/ordering/aggregation logic used by
/// <c>GET /api/receivables/history</c>.
/// <para>
/// This codebase does not extract handler bodies into separately-testable classes, so these
/// tests construct real <see cref="BillEntry"/> objects and run the same filter/order/sum LINQ
/// the endpoint performs, asserting on the result.
/// </para>
/// </summary>
[TestFixture]
public sealed class ReceivablesHistoryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 07, 05, 12, 0, 0, TimeSpan.Zero);

    // Mirrors the handler's optional from/to period filter (IsInForwardRange applied both ways)
    // and its status filter, then computes totals over the resulting (filtered) slice.
    private static (
        List<(int Year, int Month, decimal Receivable, bool Received)> Items,
        decimal TotalDevido, decimal TotalRecebido, decimal TotalPendente)
        ComputeHistory(
            IReadOnlyList<BillEntry> entries,
            int? fromYear = null, int? fromMonth = null,
            int? toYear = null, int? toMonth = null,
            string? status = null)
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

        filtered = status switch
        {
            "received" => filtered.Where(e => e.Received),
            "pending" => filtered.Where(e => !e.Received),
            _ => filtered,
        };

        var items = filtered
            .Select(e => (
                e.RefYear, e.RefMonth,
                Receivable: EntryCalculations.Receivable(
                    EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot),
                e.Received))
            .OrderByDescending(i => i.RefYear)
            .ThenByDescending(i => i.RefMonth)
            .Select(i => (i.RefYear, i.RefMonth, i.Receivable, i.Received))
            .ToList();

        var totalDevido = items.Sum(i => i.Receivable);
        var totalRecebido = items.Where(i => i.Received).Sum(i => i.Receivable);
        var totalPendente = items.Where(i => !i.Received).Sum(i => i.Receivable);

        return (items, totalDevido, totalRecebido, totalPendente);
    }

    // --- Totals invariant ---

    [Test]
    public void Totals_RecebidoPlusPendente_EqualsDevido()
    {
        // Arrange
        var received = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        received.MarkReceived(FixedNow);
        var pending = BillEntry.Create(1L, 11L, 2026, 2, 400m, 0.5m, 99L, FixedNow);

        // Act
        var (_, totalDevido, totalRecebido, totalPendente) = ComputeHistory([received, pending]);

        // Assert
        Assert.That(totalDevido, Is.EqualTo(totalRecebido + totalPendente));
    }

    // --- Status filter ---

    [Test]
    public void StatusFilter_Received_ReturnsOnlyReceivedItems()
    {
        // Arrange
        var received = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        received.MarkReceived(FixedNow);
        var pending = BillEntry.Create(1L, 11L, 2026, 2, 400m, 0.5m, 99L, FixedNow);

        // Act
        var (items, _, _, _) = ComputeHistory([received, pending], status: "received");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Received, Is.True);
        });
    }

    [Test]
    public void StatusFilter_Pending_ReturnsOnlyPendingItems()
    {
        // Arrange
        var received = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        received.MarkReceived(FixedNow);
        var pending = BillEntry.Create(1L, 11L, 2026, 2, 400m, 0.5m, 99L, FixedNow);

        // Act
        var (items, _, _, _) = ComputeHistory([received, pending], status: "pending");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Received, Is.False);
        });
    }

    [Test]
    public void StatusFilter_UnrecognizedValue_BehavesLikeAll()
    {
        // Arrange — an unknown/garbage status value should default to "all", not reject or filter everything
        var received = BillEntry.Create(1L, 10L, 2026, 1, 1000m, 0.5m, 99L, FixedNow);
        received.MarkReceived(FixedNow);
        var pending = BillEntry.Create(1L, 11L, 2026, 2, 400m, 0.5m, 99L, FixedNow);

        // Act
        var (items, _, _, _) = ComputeHistory([received, pending], status: "bogus");

        // Assert
        Assert.That(items, Has.Count.EqualTo(2));
    }

    // --- Period range filtering ---

    [Test]
    public void PeriodFilter_FromBoundary_IsInclusive()
    {
        // Arrange — one entry exactly at the "from" boundary, one before it
        var atBoundary = BillEntry.Create(1L, 10L, 2026, 3, 100m, 0.5m, 99L, FixedNow);
        var before = BillEntry.Create(1L, 11L, 2026, 2, 100m, 0.5m, 99L, FixedNow);

        // Act
        var (items, _, _, _) = ComputeHistory([atBoundary, before], fromYear: 2026, fromMonth: 3);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Month, Is.EqualTo(3));
        });
    }

    [Test]
    public void PeriodFilter_ToBoundary_IsInclusive()
    {
        // Arrange — one entry exactly at the "to" boundary, one after it
        var atBoundary = BillEntry.Create(1L, 10L, 2026, 6, 100m, 0.5m, 99L, FixedNow);
        var after = BillEntry.Create(1L, 11L, 2026, 7, 100m, 0.5m, 99L, FixedNow);

        // Act
        var (items, _, _, _) = ComputeHistory([atBoundary, after], toYear: 2026, toMonth: 6);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Month, Is.EqualTo(6));
        });
    }

    [Test]
    public void PeriodFilter_FromAndToWindow_ExcludesOutsideEntries()
    {
        // Arrange — entries in Feb (before), May (inside), Sep (after) a Mar-Jun window
        var before = BillEntry.Create(1L, 10L, 2026, 2, 100m, 0.5m, 99L, FixedNow);
        var inside = BillEntry.Create(1L, 11L, 2026, 5, 200m, 0.5m, 99L, FixedNow);
        var after = BillEntry.Create(1L, 12L, 2026, 9, 300m, 0.5m, 99L, FixedNow);

        // Act
        var (items, _, _, _) = ComputeHistory(
            [before, inside, after], fromYear: 2026, fromMonth: 3, toYear: 2026, toMonth: 6);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Month, Is.EqualTo(5));
        });
    }

    // --- Default ordering ---

    [Test]
    public void DefaultOrdering_IsYearThenMonthDescending()
    {
        // Arrange — out-of-order input across two years
        var jan2026 = BillEntry.Create(1L, 10L, 2026, 1, 100m, 0.5m, 99L, FixedNow);
        var dec2025 = BillEntry.Create(1L, 11L, 2025, 12, 100m, 0.5m, 99L, FixedNow);
        var jun2026 = BillEntry.Create(1L, 12L, 2026, 6, 100m, 0.5m, 99L, FixedNow);

        // Act
        var (items, _, _, _) = ComputeHistory([jan2026, dec2025, jun2026]);

        // Assert — most recent (year, month) first
        Assert.Multiple(() =>
        {
            Assert.That(items[0], Is.EqualTo((2026, 6, 50m, false)));
            Assert.That(items[1], Is.EqualTo((2026, 1, 50m, false)));
            Assert.That(items[2], Is.EqualTo((2025, 12, 50m, false)));
        });
    }
}
