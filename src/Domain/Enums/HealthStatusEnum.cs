namespace Domain.Enums;

/// <summary>
/// The overall or per-check outcome reported by the readiness probe.
/// </summary>
public enum HealthStatusEnum
{
    /// <summary>Everything checked is working as expected.</summary>
    Healthy,

    /// <summary>The service can still serve requests, but something needs attention (e.g. pending migrations).</summary>
    Degraded,

    /// <summary>A critical dependency is unavailable; the service cannot reliably serve requests.</summary>
    Unhealthy
}
