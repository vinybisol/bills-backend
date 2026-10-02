using Domain.Entities;

namespace Domain.UnitTests.TestSupport;

/// <summary>Builders for bill/income entries in a given state, for calculation tests.</summary>
internal static class Entries
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset PaidAt = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    public static BillEntry Bill(
        decimal planned,
        decimal split = 1m,
        decimal? actual = null,
        bool paid = false,
        bool received = false,
        int year = 2026,
        int month = 7,
        long billId = 9L)
    {
        var entry = BillEntry.Create(1L, billId, year, month, planned, split, split < 1m ? 5L : null, CreatedAt);

        if (paid)
            entry.MarkPaid(PaidAt, actual);
        else if (actual.HasValue)
            entry.UpdateAmounts(null, actual);

        if (received)
            entry.MarkReceived(PaidAt);

        return entry;
    }

    public static IncomeEntry Income(decimal planned, decimal? actual = null, bool received = false, int month = 7)
    {
        var entry = IncomeEntry.Create(1L, 3L, 2026, month, planned, CreatedAt);

        if (received)
            entry.MarkReceived(PaidAt, actual);
        else if (actual.HasValue)
            entry.UpdateAmounts(null, actual);

        return entry;
    }
}
