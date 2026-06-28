using BillsBackend.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.UnitTests;

/// <summary>
/// Shared helpers for the unit-test fixtures.
/// </summary>
internal static class TestSupport
{
    /// <summary>
    /// Creates an <see cref="AppDbContext"/> backed by an isolated in-memory database.
    /// </summary>
    /// <returns>A fresh context whose store is unique to the caller.</returns>
    public static AppDbContext NewInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"unit-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }
}

/// <summary>
/// A <see cref="TimeProvider"/> that always returns a fixed instant, for deterministic tests.
/// </summary>
/// <param name="now">The instant to return from <see cref="GetUtcNow"/>.</param>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => now;
}
