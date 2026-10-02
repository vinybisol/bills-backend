using Domain.Enums;

namespace Application.DTOs.Services;

/// <summary>The payload returned by income template read operations.</summary>
/// <param name="Id">The internal income id.</param>
/// <param name="Name">The income template display name.</param>
/// <param name="Kind">The income kind.</param>
/// <param name="DefaultAmount">The default planned amount.</param>
public sealed record IncomeDto(long Id, string Name, IncomeKindEnum Kind, decimal DefaultAmount);
