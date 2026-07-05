namespace BillsBackend.UnitTests;

/// <summary>
/// A <see cref="TimeProvider"/> that always returns a fixed instant, for deterministic tests.
/// </summary>
/// <param name="now">The instant to return from <see cref="GetUtcNow"/>.</param>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => now;
}
