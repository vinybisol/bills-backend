using Domain.Enums;

namespace Application.DTOs.Services;

/// <summary>The outcome of a single readiness check.</summary>
/// <param name="Name">The stable, machine-readable check name (e.g. <c>"postgres"</c>).</param>
/// <param name="Status">The check outcome.</param>
/// <param name="DurationMs">How long the check took, in milliseconds.</param>
/// <param name="Description">
/// A short, generic description; never exposes exception messages, host names or migration names.
/// </param>
public sealed record HealthCheckDto(string Name, HealthStatusEnum Status, long DurationMs, string? Description);

/// <summary>The full readiness report returned by <c>GET /health/ready</c>.</summary>
/// <param name="Status">The worst status across all checks.</param>
/// <param name="TotalDurationMs">How long the whole readiness probe took, in milliseconds.</param>
/// <param name="Checks">The individual check outcomes.</param>
public sealed record HealthReportDto(HealthStatusEnum Status, long TotalDurationMs, IReadOnlyList<HealthCheckDto> Checks);
