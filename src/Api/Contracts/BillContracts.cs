using Domain.Enums;

namespace Api.Contracts;

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

/// <summary>The request body for <c>POST /bills/{billId}/recalculate</c>.</summary>
/// <param name="FromYear">The reference year from which to start recalculation (inclusive).</param>
/// <param name="FromMonth">The reference month from which to start recalculation (1–12, inclusive).</param>
/// <param name="NewAmount">The new planned amount to apply; must be zero or greater.</param>
internal sealed record RecalculateRequest(int FromYear, int FromMonth, decimal NewAmount);
