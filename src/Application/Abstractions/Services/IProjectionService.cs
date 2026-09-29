using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IProjectionService
{
    /// <summary>
    /// Generates the monthly entries of <paramref name="year"/> for every active recurring bill and
    /// income template of the current owner. Idempotent: months that already have an entry are skipped.
    /// </summary>
    Task<Result<ProjectionDto>> ProjectYearAsync(int year, CancellationToken cancellationToken);
}
