using Domain.Enums;

namespace Application.DTOs.Services;

/// <summary>The payload returned by bill template read operations.</summary>
/// <param name="Id">The internal bill id.</param>
/// <param name="Name">The bill template display name.</param>
/// <param name="CategoryId">The category this bill belongs to.</param>
/// <param name="Kind">The bill kind.</param>
/// <param name="DefaultAmount">The default planned amount.</param>
/// <param name="SplitRatio">The owner's fraction of the expense (0 to 1).</param>
/// <param name="PersonId">The person who owes the remaining fraction, or <see langword="null"/> when SplitRatio is 1.</param>
public sealed record BillDto(long Id, string Name, long CategoryId, BillKindEnum Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

/// <summary>The result of <c>POST /bills/{billId}/recalculate</c>.</summary>
/// <param name="BillId">The recalculated bill's id.</param>
/// <param name="UpdatedEntries">The number of unpaid entries whose planned amount was updated.</param>
/// <param name="SkippedPaid">The number of paid (frozen) entries in range that were left untouched.</param>
/// <param name="NewDefaultAmount">The new default amount now set on the bill template.</param>
public sealed record BillRecalculationDto(long BillId, int UpdatedEntries, int SkippedPaid, decimal NewDefaultAmount);

/// <summary>
/// The header of a bill's history: template data resolved even when the template, its category or
/// its person have since been deactivated.
/// </summary>
/// <param name="BillId">The bill template's id.</param>
/// <param name="Name">The bill template's display name.</param>
/// <param name="Category">The bill's category display name.</param>
/// <param name="SplitRatio">The bill template's current split ratio.</param>
/// <param name="Person">The name of the person who owes the split, or <see langword="null"/>.</param>
public sealed record BillHistoryHeaderDto(long BillId, string Name, string Category, decimal SplitRatio, string? Person);

/// <summary>The period-over-period variation of a bill history item.</summary>
/// <param name="Abs">Absolute change in effective amount vs. the previous item.</param>
/// <param name="Pct">Percentage change vs. the previous item, or <see langword="null"/> when the previous effective amount was zero.</param>
public sealed record BillHistoryVariationDto(decimal Abs, decimal? Pct);

/// <summary>A single monthly item of a bill's history.</summary>
public sealed record BillHistoryItemDto(
    int Year, int Month, decimal PlannedAmount, decimal? ActualAmount, decimal Effective, decimal MyShare,
    bool Paid, DateTimeOffset? PaidDate, BillHistoryVariationDto? Variation);

/// <summary>Aggregates over the (filtered) bill history items.</summary>
public sealed record BillHistorySummaryDto(
    decimal AvgEffective, decimal MinEffective, decimal MaxEffective, decimal TotalPaidMyShare);

/// <summary>The complete bill history: template header, summary and chronological items.</summary>
public sealed record BillHistoryDto(
    long BillId, string Name, string Category, decimal SplitRatio, string? Person,
    BillHistorySummaryDto Summary, IReadOnlyList<BillHistoryItemDto> Items);
