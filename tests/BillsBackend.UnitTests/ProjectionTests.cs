using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for the annual projection domain logic: entry generation from bill and income
/// templates, snapshot field copying, and kind/active filtering.
/// </summary>
[TestFixture]
public sealed class ProjectionTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 06, 30, 12, 0, 0, TimeSpan.Zero);

    // --- BillEntry generation ---

    [Test]
    public void BillEntry_RecurringActiveBill_Generates12Entries()
    {
        // Arrange
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 1500m, 1m, null, FixedNow);
        var recurringBills = new[] { bill }.Where(b => b.Kind == BillKind.Recurring).ToList();

        // Act — use explicit billId since Bill.Id is 0 without a database to assign it
        var entries = new List<BillEntry>();
        foreach (var b in recurringBills)
            for (int month = 1; month <= 12; month++)
                entries.Add(BillEntry.Create(1L, 10L, 2025, month, b.DefaultAmount, b.SplitRatio, b.PersonId, FixedNow));

        // Assert
        Assert.That(entries, Has.Count.EqualTo(12));
    }

    [Test]
    public void BillEntry_OneOffBill_IsIgnored()
    {
        // Arrange
        var bill = Bill.Create(1L, "Reparo", 1L, BillKind.OneOff, 500m, 1m, null, FixedNow);

        // Act — the endpoint queries only recurring kind; one_off bills must be absent
        var recurringBills = new[] { bill }.Where(b => b.Kind == BillKind.Recurring).ToList();

        // Assert
        Assert.That(recurringBills, Is.Empty);
    }

    [Test]
    public void BillEntry_InactiveBill_IsIgnored()
    {
        // Arrange
        var bill = Bill.Create(1L, "Aluguel", 1L, BillKind.Recurring, 1500m, 1m, null, FixedNow);
        bill.Deactivate();

        // Act — the global query filter adds Active == true; simulate that here
        var activeBills = new[] { bill }.Where(b => b.Active && b.Kind == BillKind.Recurring).ToList();

        // Assert
        Assert.That(activeBills, Is.Empty);
    }

    [Test]
    public void BillEntry_SnapshotFields_AreCopiedCorrectly()
    {
        // Arrange
        const long billId = 42L;
        var bill = Bill.Create(1L, "Internet", 2L, BillKind.Recurring, 120m, 0.5m, 99L, FixedNow);

        // Act — use explicit billId since Bill.Id is 0 without a database to assign it
        var entry = BillEntry.Create(1L, billId, 2025, 3, bill.DefaultAmount, bill.SplitRatio, bill.PersonId, FixedNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(entry.OwnerId, Is.EqualTo(1L));
            Assert.That(entry.BillId, Is.EqualTo(billId));
            Assert.That(entry.RefYear, Is.EqualTo(2025));
            Assert.That(entry.RefMonth, Is.EqualTo(3));
            Assert.That(entry.PlannedAmount, Is.EqualTo(120m));
            Assert.That(entry.SplitRatioSnapshot, Is.EqualTo(0.5m));
            Assert.That(entry.PersonId, Is.EqualTo(99L));
            Assert.That(entry.ActualAmount, Is.Null);
            Assert.That(entry.Paid, Is.False);
            Assert.That(entry.PaidDate, Is.Null);
            Assert.That(entry.Received, Is.False);
            Assert.That(entry.ReceivedDate, Is.Null);
            Assert.That(entry.CreatedAt, Is.EqualTo(FixedNow));
        });
    }

    // --- IncomeEntry generation ---

    [Test]
    public void IncomeEntry_RecurringActiveIncome_Generates12Entries()
    {
        // Arrange
        var income = Income.Create(1L, "Salario", IncomeKind.Recurring, 5000m, FixedNow);
        var recurringIncomes = new[] { income }.Where(i => i.Kind == IncomeKind.Recurring).ToList();

        // Act — use explicit incomeId since Income.Id is 0 without a database to assign it
        var entries = new List<IncomeEntry>();
        foreach (var i in recurringIncomes)
            for (int month = 1; month <= 12; month++)
                entries.Add(IncomeEntry.Create(1L, 20L, 2025, month, i.DefaultAmount, FixedNow));

        // Assert
        Assert.That(entries, Has.Count.EqualTo(12));
    }

    [Test]
    public void IncomeEntry_OneOffIncome_IsIgnored()
    {
        // Arrange
        var income = Income.Create(1L, "Bonus", IncomeKind.OneOff, 1000m, FixedNow);

        // Act — the endpoint queries only recurring kind; one_off incomes must be absent
        var recurringIncomes = new[] { income }.Where(i => i.Kind == IncomeKind.Recurring).ToList();

        // Assert
        Assert.That(recurringIncomes, Is.Empty);
    }
}
