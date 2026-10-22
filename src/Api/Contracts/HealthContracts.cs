using Domain.Enums;

namespace Api.Contracts;

/// <summary>Response body for <c>GET /health/live</c>.</summary>
internal sealed record LivenessResponse(HealthStatusEnum Status);
