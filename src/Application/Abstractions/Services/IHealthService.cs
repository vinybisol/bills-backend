using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IHealthService
{
    /// <summary>
    /// Probes the service's critical dependencies (database connectivity, pending migrations)
    /// and reports the worst status found. Dependency failures are reported as data — this
    /// method always succeeds.
    /// </summary>
    /// <param name="ct">A token the caller can use to cancel the whole probe.</param>
    Task<Result<HealthReportDto>> GetReadinessAsync(CancellationToken ct);
}
