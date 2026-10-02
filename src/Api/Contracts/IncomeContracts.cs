using Domain.Enums;

namespace Api.Contracts;

/// <summary>The request body for <c>POST /incomes</c>.</summary>
/// <param name="Name">The income template name.</param>
/// <param name="Kind">The income kind.</param>
/// <param name="DefaultAmount">The default planned amount; must be zero or greater.</param>
internal sealed record CreateIncomeRequest(string Name, IncomeKindEnum Kind, decimal DefaultAmount);

/// <summary>The request body for <c>PUT /incomes/{id}</c>.</summary>
/// <param name="Name">The new income template name.</param>
/// <param name="Kind">The new income kind.</param>
/// <param name="DefaultAmount">The new default planned amount; must be zero or greater.</param>
internal sealed record UpdateIncomeRequest(string Name, IncomeKindEnum Kind, decimal DefaultAmount);
