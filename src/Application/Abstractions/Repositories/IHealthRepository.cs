namespace Application.Abstractions.Repositories;

/// <summary>
/// Reports low-level facts about the database dependency, isolating <see cref="Application"/>
/// services from the concrete persistence technology (EF Core).
/// </summary>
public interface IHealthRepository
{
    /// <summary>
    /// Checks whether the database is reachable right now.
    /// </summary>
    /// <param name="ct">A token used to cancel the check.</param>
    Task<bool> CanConnectAsync(CancellationToken ct);

    /// <summary>
    /// Counts the EF Core migrations that have not been applied to the database yet.
    /// </summary>
    /// <param name="ct">A token used to cancel the check.</param>
    Task<int> CountPendingMigrationsAsync(CancellationToken ct);
}
