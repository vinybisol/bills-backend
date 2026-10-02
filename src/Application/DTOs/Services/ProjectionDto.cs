namespace Application.DTOs.Services;

/// <summary>The payload returned by <c>POST /api/v1/projection/{year}</c>.</summary>
/// <param name="Year">The year for which the projection was generated.</param>
/// <param name="BillEntriesCreated">The number of new bill entries created.</param>
/// <param name="IncomeEntriesCreated">The number of new income entries created.</param>
/// <param name="Skipped">The number of entries that already existed and were skipped.</param>
public sealed record ProjectionDto(int Year, int BillEntriesCreated, int IncomeEntriesCreated, int Skipped);
