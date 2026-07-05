using BillsBackend.Api.Domain;

namespace BillsBackend.Api.Contracts;

/// <summary>The payload returned by income read operations.</summary>
/// <param name="Id">The internal income id.</param>
/// <param name="Name">The income template display name.</param>
/// <param name="Kind">The income kind.</param>
/// <param name="DefaultAmount">The default planned amount.</param>
internal sealed record IncomeDto(long Id, string Name, IncomeKind Kind, decimal DefaultAmount);

/// <summary>The request body for <c>POST /incomes</c>.</summary>
/// <param name="Name">The income template name.</param>
/// <param name="Kind">The income kind.</param>
/// <param name="DefaultAmount">The default planned amount; must be zero or greater.</param>
internal sealed record CreateIncomeRequest(string Name, IncomeKind Kind, decimal DefaultAmount);

/// <summary>The request body for <c>PUT /incomes/{id}</c>.</summary>
/// <param name="Name">The new income template name.</param>
/// <param name="Kind">The new income kind.</param>
/// <param name="DefaultAmount">The new default planned amount; must be zero or greater.</param>
internal sealed record UpdateIncomeRequest(string Name, IncomeKind Kind, decimal DefaultAmount);
