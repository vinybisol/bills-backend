using BillsBackend.Api.Domain;
using Domain.Entities;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the receivables-month aggregation logic used by <c>GET /api/receivables/month</c>,
/// plus the <see cref="BillEntry.UnmarkReceived"/> domain method it relies on.
/// <para>
/// This codebase does not extract handler bodies into separately-testable classes, so the
/// aggregation tests construct real <see cref="BillEntry"/> objects and run the same
/// filter/group/sum LINQ the endpoint performs, asserting on the result.
/// </para>
/// </summary>
[TestFixture]
public sealed class ReceivablesMonthTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 07, 05, 12, 0, 0, TimeSpan.Zero);

    // Mirrors the handler's panel filter: SplitRatioSnapshot < 1 and PersonId assigned.
    private static List<BillEntry> FilterReceivables(IEnumerable<BillEntry> entries) =>
        entries.Where(e => e.SplitRatioSnapshot < 1 && e.PersonId is not null).ToList();

    // Mirrors the handler's per-person aggregation.
    private static (decimal TotalDevido, decimal JaRecebido, decimal Pendente) Aggregate(IEnumerable<BillEntry> personEntries)
    {
        var receivables = personEntries
            .Select(e => (
                Receivable: EntryCalculations.Receivable(
                    EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot),
                e.Received))
            .ToList();

        var totalDevido = receivables.Sum(r => r.Receivable);
        var jaRecebido = receivables.Where(r => r.Received).Sum(r => r.Receivable);
        var pendente = receivables.Where(r => !r.Received).Sum(r => r.Receivable);
        return (totalDevido, jaRecebido, pendente);
    }

    // --- Receivable formula ---

    [Test]
    public void Receivable_SplitHalf_IsHalfOfEffectiveAmount()
    {
        // Arrange — split 0.5: half is mine, half is the other person's
        var entry = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.5m, 99L, FixedNow);

        // Act
        var receivable = EntryCalculations.Receivable(
            EntryCalculations.EffectiveAmount(entry.PlannedAmount, entry.ActualAmount), entry.SplitRatioSnapshot);

        // Assert
        Assert.That(receivable, Is.EqualTo(500m));
    }

    [Test]
    public void Receivable_SplitZero_IsFullEffectiveAmount()
    {
        // Arrange — split 0.0: the whole amount passes through the owner, fully the other person's
        var entry = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.0m, 99L, FixedNow);

        // Act
        var receivable = EntryCalculations.Receivable(
            EntryCalculations.EffectiveAmount(entry.PlannedAmount, entry.ActualAmount), entry.SplitRatioSnapshot);

        // Assert — still a receivable, just the entire amount
        Assert.That(receivable, Is.EqualTo(1000m));
    }

    // --- Panel filter ---

    [Test]
    public void FilterReceivables_ExcludesEntriesWithFullSplit()
    {
        // Arrange — one shared entry (split 0.5) and one fully-owner entry (split 1.0)
        var shared = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.5m, 99L, FixedNow);
        var fullyMine = BillEntry.Create(1L, 11L, 2026, 7, 500m, 1m, null, FixedNow);

        // Act
        var receivables = FilterReceivables([shared, fullyMine]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(receivables, Has.Count.EqualTo(1));
            Assert.That(receivables[0], Is.SameAs(shared));
        });
    }

    [Test]
    public void FilterReceivables_ZeroSplitStillIncluded()
    {
        // Arrange
        var passThrough = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.0m, 99L, FixedNow);
        var fullyMine = BillEntry.Create(1L, 11L, 2026, 7, 500m, 1m, null, FixedNow);

        // Act
        var receivables = FilterReceivables([passThrough, fullyMine]);

        // Assert
        Assert.That(receivables, Has.Count.EqualTo(1));
    }

    // --- Per-person aggregation ---

    [Test]
    public void Aggregate_TotalDevido_EqualsJaRecebidoPlusPendente()
    {
        // Arrange — two entries for the same person, one received, one not
        var received = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.5m, 99L, FixedNow);
        received.MarkReceived(FixedNow);
        var pending = BillEntry.Create(1L, 11L, 2026, 7, 400m, 0.5m, 99L, FixedNow);

        // Act
        var (totalDevido, jaRecebido, pendente) = Aggregate([received, pending]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(totalDevido, Is.EqualTo(jaRecebido + pendente));
            Assert.That(jaRecebido, Is.EqualTo(500m));
            Assert.That(pendente, Is.EqualTo(200m));
        });
    }

    [Test]
    public void Aggregate_NothingReceived_JaRecebidoIsZero()
    {
        // Arrange
        var pending = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.5m, 99L, FixedNow);

        // Act
        var (totalDevido, jaRecebido, pendente) = Aggregate([pending]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(jaRecebido, Is.EqualTo(0m));
            Assert.That(pendente, Is.EqualTo(totalDevido));
        });
    }

    // --- BillEntry.UnmarkReceived ---

    [Test]
    public void UnmarkReceived_ClearsReceivedAndReceivedDate()
    {
        // Arrange
        var entry = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.5m, 99L, FixedNow);
        entry.MarkReceived(FixedNow);
        Assert.That(entry.Received, Is.True);

        // Act
        entry.UnmarkReceived();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.Received, Is.False);
            Assert.That(entry.ReceivedDate, Is.Null);
        });
    }

    [Test]
    public void UnmarkReceived_DoesNotTouchPaidOrPaidDate()
    {
        // Arrange — the entry is both paid (by the owner) and received (by the other person)
        var entry = BillEntry.Create(1L, 10L, 2026, 7, 1000m, 0.5m, 99L, FixedNow);
        entry.MarkPaid(FixedNow);
        entry.MarkReceived(FixedNow);

        // Act — unmarking "received" must be independent of "paid"
        entry.UnmarkReceived();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.Paid, Is.True);
            Assert.That(entry.PaidDate, Is.EqualTo(FixedNow));
            Assert.That(entry.Received, Is.False);
        });
    }
}
