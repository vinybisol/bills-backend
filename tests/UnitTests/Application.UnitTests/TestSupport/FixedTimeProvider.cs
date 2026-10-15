namespace Application.UnitTests.TestSupport;

/// <summary>A <see cref="TimeProvider"/> that always returns the same instant, for deterministic tests.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
