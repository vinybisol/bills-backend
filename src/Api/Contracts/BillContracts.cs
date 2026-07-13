using Domain.Enums;

namespace BillsBackend.Api.Contracts;

/// <summary>The payload returned by bill read operations.</summary>
/// <param name="Id">The internal bill id.</param>
/// <param name="Name">The bill template display name.</param>
/// <param name="CategoryId">The category this bill belongs to.</param>
/// <param name="Kind">The bill kind.</param>
/// <param name="DefaultAmount">The default planned amount.</param>
/// <param name="SplitRatio">The owner's fraction of the expense (0 to 1).</param>
/// <param name="PersonId">The person who owes the remaining fraction, or <see langword="null"/> when SplitRatio is 1.</param>
internal sealed record BillDto(long Id, string Name, long CategoryId, BillKindEnum Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

/// <summary>The request body for <c>POST /bills</c>.</summary>
/// <param name="Name">The bill template name.</param>
/// <param name="CategoryId">The category this bill belongs to.</param>
/// <param name="Kind">The bill kind.</param>
/// <param name="DefaultAmount">The default planned amount; must be zero or greater.</param>
/// <param name="SplitRatio">The owner's fraction of the expense; must be in [0, 1].</param>
/// <param name="PersonId">Required when SplitRatio is less than 1; must be null when SplitRatio is 1.</param>
internal sealed record CreateBillRequest(string Name, long CategoryId, BillKindEnum Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

/// <summary>The request body for <c>PUT /bills/{id}</c>.</summary>
/// <param name="Name">The new bill template name.</param>
/// <param name="CategoryId">The new category.</param>
/// <param name="Kind">The new bill kind.</param>
/// <param name="DefaultAmount">The new default planned amount; must be zero or greater.</param>
/// <param name="SplitRatio">The new owner fraction; must be in [0, 1].</param>
/// <param name="PersonId">Required when SplitRatio is less than 1; must be null when SplitRatio is 1.</param>
internal sealed record UpdateBillRequest(string Name, long CategoryId, BillKindEnum Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

/// <summary>The request body for <c>POST /api/bills/{billId}/recalculate</c>.</summary>
/// <param name="FromYear">The reference year from which to start recalculation (inclusive).</param>
/// <param name="FromMonth">The reference month from which to start recalculation (1–12, inclusive).</param>
/// <param name="NewAmount">The new planned amount to apply; must be zero or greater.</param>
internal sealed record RecalculateRequest(int FromYear, int FromMonth, decimal NewAmount);

/// <summary>The response returned by <c>POST /api/bills/{billId}/recalculate</c>.</summary>
/// <param name="BillId">The recalculated bill's id.</param>
/// <param name="UpdatedEntries">The number of unpaid entries whose planned amount was updated.</param>
/// <param name="SkippedPaid">The number of paid entries in range that were left untouched.</param>
/// <param name="NewDefaultAmount">The new default amount now set on the bill template.</param>
internal sealed record RecalculateResponse(long BillId, int UpdatedEntries, int SkippedPaid, decimal NewDefaultAmount);

/// <summary>The period-over-period variation for a single item in <c>GET /api/bills/{billId}/history</c>.</summary>
/// <param name="Abs">Absolute change in <see cref="BillHistoryItemDto.Effective"/> vs. the previous item.</param>
/// <param name="Pct">
/// Percentage change vs. the previous item, or <see langword="null"/> when the previous
/// effective amount was zero.
/// </param>
internal sealed record BillHistoryVariationDto(decimal Abs, decimal? Pct);

/// <summary>A single monthly item returned by <c>GET /api/bills/{billId}/history</c>.</summary>
/// <param name="Year">The entry's reference year.</param>
/// <param name="Month">The entry's reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount snapshotted for this month.</param>
/// <param name="ActualAmount">The actual amount, or <see langword="null"/> if not yet confirmed.</param>
/// <param name="Effective">Actual amount when present, otherwise planned.</param>
/// <param name="MyShare">The owner's share of <see cref="Effective"/>, using the split ratio snapshotted at projection time.</param>
/// <param name="Paid">Whether the owner has paid this expense.</param>
/// <param name="PaidDate">The UTC instant the expense was paid, or <see langword="null"/>.</param>
/// <param name="Variation">The change vs. the previous item in chronological order, or <see langword="null"/> for the first item.</param>
internal sealed record BillHistoryItemDto(
    int Year, int Month, decimal PlannedAmount, decimal? ActualAmount, decimal Effective, decimal MyShare,
    bool Paid, DateTimeOffset? PaidDate, BillHistoryVariationDto? Variation);

/// <summary>Aggregated totals returned by <c>GET /api/bills/{billId}/history</c>, computed over the filtered slice.</summary>
/// <param name="AvgEffective">The average <see cref="BillHistoryItemDto.Effective"/> across items; zero when there are none.</param>
/// <param name="MinEffective">The minimum <see cref="BillHistoryItemDto.Effective"/> across items; zero when there are none.</param>
/// <param name="MaxEffective">The maximum <see cref="BillHistoryItemDto.Effective"/> across items; zero when there are none.</param>
/// <param name="TotalPaidMyShare">The sum of <see cref="BillHistoryItemDto.MyShare"/> across paid items only.</param>
internal sealed record BillHistorySummaryDto(
    decimal AvgEffective, decimal MinEffective, decimal MaxEffective, decimal TotalPaidMyShare);

/// <summary>The complete response returned by <c>GET /api/bills/{billId}/history</c>.</summary>
/// <param name="BillId">The requested bill template's id.</param>
/// <param name="Name">The bill template's display name.</param>
/// <param name="Category">The bill's category display name.</param>
/// <param name="SplitRatio">The bill template's current split ratio (not a per-entry snapshot).</param>
/// <param name="Person">The name of the person who owes the split, or <see langword="null"/> when the bill is not shared.</param>
/// <param name="Summary">Aggregates computed over whatever period filter was applied.</param>
/// <param name="Items">Item-level rows, ordered by year then month ascending (chronological).</param>
internal sealed record BillHistoryDto(
    long BillId, string Name, string Category, decimal SplitRatio, string? Person,
    BillHistorySummaryDto Summary, IReadOnlyList<BillHistoryItemDto> Items);
