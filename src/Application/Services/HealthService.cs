using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Enums;

namespace Application.Services;

internal sealed class HealthService(IHealthRepository repository, TimeProvider timeProvider) : IHealthService
{
    internal const string DatabaseCheck = "postgres";
    internal const string MigrationsCheck = "migrations";

    /// <summary>Per-check timeout. Overridable (internal) so tests can keep timeout scenarios fast.</summary>
    internal TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<Result<HealthReportDto>> GetReadinessAsync(CancellationToken ct)
    {
        var start = timeProvider.GetTimestamp();

        var postgresCheck = await RunCheckAsync(
            DatabaseCheck,
            async token =>
            {
                var canConnect = await repository.CanConnectAsync(token);
                return canConnect
                    ? (HealthStatusEnum.Healthy, (string?)null)
                    : (HealthStatusEnum.Unhealthy, "Database unreachable.");
            },
            ct);

        var migrationsCheck = postgresCheck.Status == HealthStatusEnum.Healthy
            ? await RunCheckAsync(
                MigrationsCheck,
                async token =>
                {
                    var pending = await repository.CountPendingMigrationsAsync(token);
                    return pending == 0
                        ? (HealthStatusEnum.Healthy, (string?)null)
                        : (HealthStatusEnum.Degraded, "Pending migrations detected.");
                },
                ct)
            : new HealthCheckDto(MigrationsCheck, HealthStatusEnum.Unhealthy, 0, "Skipped: database unreachable.");

        HealthCheckDto[] checks = [postgresCheck, migrationsCheck];
        var overall = checks.Max(c => c.Status);
        var totalDuration = timeProvider.GetElapsedTime(start);

        return Result.Success(new HealthReportDto(overall, (long)totalDuration.TotalMilliseconds, checks));
    }

    private async Task<HealthCheckDto> RunCheckAsync(
        string name,
        Func<CancellationToken, Task<(HealthStatusEnum Status, string? Description)>> check,
        CancellationToken ct)
    {
        var start = timeProvider.GetTimestamp();
        using var timeoutCts = new CancellationTokenSource(CheckTimeout, timeProvider);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var (status, description) = await check(cts.Token);
            return new HealthCheckDto(name, status, ElapsedMs(start), description);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled, not our timeout — this is not a health fact, propagate it.
            throw;
        }
        catch (OperationCanceledException)
        {
            return new HealthCheckDto(name, HealthStatusEnum.Unhealthy, ElapsedMs(start), "Check timed out.");
        }
        catch (Exception)
        {
            // Never surface ex.Message: it may contain connection strings, host names, etc.
            return new HealthCheckDto(name, HealthStatusEnum.Unhealthy, ElapsedMs(start), "Check failed.");
        }
    }

    private long ElapsedMs(long start) => (long)timeProvider.GetElapsedTime(start).TotalMilliseconds;
}
