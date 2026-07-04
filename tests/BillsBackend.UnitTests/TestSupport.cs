using BillsBackend.Api.Data;
using BillsBackend.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.UnitTests;

/// <summary>
/// Shared helpers for the unit-test fixtures.
/// </summary>
internal static class TestSupport
{
    /// <summary>
    /// Creates an <see cref="AppDbContext"/> backed by an isolated in-memory database,
    /// using a new <see cref="TestCurrentOwner"/> with <c>Id = 0</c>.
    /// </summary>
    /// <returns>A fresh context whose store is unique to the caller.</returns>
    public static AppDbContext NewInMemoryContext() =>
        NewInMemoryContext(new TestCurrentOwner());

    /// <summary>
    /// Creates an <see cref="AppDbContext"/> backed by an isolated in-memory database,
    /// using the supplied <paramref name="currentOwner"/> for query-filter scoping.
    /// Callers retain the reference so they can update <see cref="ICurrentOwner.Id"/>
    /// after provisioning.
    /// </summary>
    /// <param name="currentOwner">The owner context used to scope filtered queries.</param>
    /// <returns>A fresh context whose store is unique to the caller.</returns>
    public static AppDbContext NewInMemoryContext(ICurrentOwner currentOwner)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"unit-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, currentOwner);
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

/// <summary>
/// A simple mutable <see cref="ICurrentOwner"/> implementation for unit tests.
/// Tests retain a reference and set <see cref="Id"/> after provisioning to activate
/// query filters for a specific owner.
/// </summary>
internal sealed class TestCurrentOwner : ICurrentOwner
{
    /// <inheritdoc/>
    public long Id { get; set; }

    public void SetCurrentOwnerId(long id)
        => Id = id;
}
