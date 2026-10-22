using Application.Abstractions.Repositories;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Enums;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class HealthServiceTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 6, 29, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FastTimeout = TimeSpan.FromMilliseconds(50);

    private IHealthRepository _repository = null!;
    private HealthService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IHealthRepository>();
        _sut = new HealthService(_repository, new FixedTimeProvider(_fixedNow)) { CheckTimeout = FastTimeout };
    }

    [Test]
    public async Task GetReadinessAsync_AllChecksHealthy_ReturnsOverallHealthyWithBothChecks()
    {
        // Arrange
        _repository.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        _repository.CountPendingMigrationsAsync(Arg.Any<CancellationToken>()).Returns(0);

        // Act
        var result = await _sut.GetReadinessAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Status, Is.EqualTo(HealthStatusEnum.Healthy));
            Assert.That(result.Value.Checks, Has.Count.EqualTo(2));
            Assert.That(result.Value.Checks.Select(c => c.Name), Is.EquivalentTo([HealthService.DatabaseCheck, HealthService.MigrationsCheck]));
            Assert.That(result.Value.Checks.Select(c => c.Status), Is.All.EqualTo(HealthStatusEnum.Healthy));
        }

    }

    [Test]
    public async Task GetReadinessAsync_CannotConnect_ReturnsOverallUnhealthyAndSkipsMigrations()
    {
        // Arrange
        _repository.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _sut.GetReadinessAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Status, Is.EqualTo(HealthStatusEnum.Unhealthy));
        }
        var migrationsCheck = result.Value.Checks.Single(c => c.Name == HealthService.MigrationsCheck);
        Assert.That(migrationsCheck.Status, Is.EqualTo(HealthStatusEnum.Unhealthy));
        await _repository.DidNotReceiveWithAnyArgs().CountPendingMigrationsAsync(default);
    }

    [Test]
    public async Task GetReadinessAsync_PendingMigrations_ReturnsOverallDegraded()
    {
        // Arrange
        _repository.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        _repository.CountPendingMigrationsAsync(Arg.Any<CancellationToken>()).Returns(3);

        // Act
        var result = await _sut.GetReadinessAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Status, Is.EqualTo(HealthStatusEnum.Degraded));
        }
        var postgresCheck = result.Value.Checks.Single(c => c.Name == HealthService.DatabaseCheck);
        var migrationsCheck = result.Value.Checks.Single(c => c.Name == HealthService.MigrationsCheck);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(postgresCheck.Status, Is.EqualTo(HealthStatusEnum.Healthy));
            Assert.That(migrationsCheck.Status, Is.EqualTo(HealthStatusEnum.Degraded));
        }

    }

    [Test]
    public async Task GetReadinessAsync_RepositoryThrows_ReturnsUnhealthyWithoutLeakingExceptionMessage()
    {
        // Arrange
        _repository.CanConnectAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("secret host xyz.neon.tech"));

        // Act
        var result = await _sut.GetReadinessAsync(CancellationToken.None);

        // Assert
        Assert.That(result.Value.Status, Is.EqualTo(HealthStatusEnum.Unhealthy));
        var allText = string.Join(' ', result.Value.Checks.Select(c => $"{c.Name} {c.Status} {c.Description}"));
        Assert.That(allText, Does.Not.Contain("secret"));
    }

    [Test]
    public async Task GetReadinessAsync_RepositoryHangs_ReportsUnhealthyAfterTimeout()
    {
        // Arrange
        _repository.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var token = ci.Arg<CancellationToken>();
            var tcs = new TaskCompletionSource<bool>();
            token.Register(() => tcs.TrySetCanceled(token));
            return tcs.Task;
        });

        // Act
        var result = await _sut.GetReadinessAsync(CancellationToken.None);

        // Assert
        var postgresCheck = result.Value.Checks.Single(c => c.Name == HealthService.DatabaseCheck);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(postgresCheck.Status, Is.EqualTo(HealthStatusEnum.Unhealthy));
            Assert.That(postgresCheck.Description, Is.EqualTo("Check timed out."));
        }
    }

    [Test]
    public void GetReadinessAsync_CallerTokenAlreadyCancelled_PropagatesOperationCanceledException()
    {
        // Arrange
        _repository.CanConnectAsync(Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromCanceled<bool>(ci.Arg<CancellationToken>()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act / Assert
        Assert.That(
            async () => await _sut.GetReadinessAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
