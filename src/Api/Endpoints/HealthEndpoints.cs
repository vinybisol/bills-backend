using System.Threading.RateLimiting;
using Api.Contracts;
using Api.Extensions;
using Application.Abstractions.Services;
using Domain.Enums;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Endpoints;

internal static class HealthEndpoints
{
    internal const string RateLimitPolicy = "health";
    internal const int RateLimitPermits = 10;
    internal static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Registers the fixed-window limiter shared by the anonymous health endpoints:
    /// at most <see cref="RateLimitPermits"/> requests per <see cref="RateLimitWindow"/>, excess → 429.
    /// </summary>
    public static IServiceCollection AddHealthRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                return ValueTask.CompletedTask;
            };
            options.AddFixedWindowLimiter(RateLimitPolicy, limiter =>
            {
                limiter.PermitLimit = RateLimitPermits;
                limiter.Window = RateLimitWindow;
                limiter.QueueLimit = 0;
            });
        });

    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder group)
    {
        var healthGroup = group
            .MapGroup("/health")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);

        healthGroup.MapGet("/live", GetLiveness).WithName("GetLiveness");
        healthGroup.MapGet("/ready", GetReadiness).WithName("GetReadiness");

        return group;
    }

    private static IResult GetLiveness() => Results.Ok(new LivenessResponse(HealthStatusEnum.Healthy));

    private static async Task<IResult> GetReadiness(
        IHealthService healthService,
        CancellationToken ct)
    {
        var result = await healthService.GetReadinessAsync(ct);
        if (result.IsFailure)
            return result.ToHttpResult();

        var report = result.Value;

        // 503 reflects the readiness data itself, not an Error — ResultExtensions has no such mapping.
        return report.Status == HealthStatusEnum.Unhealthy
            ? Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(report);
    }
}
