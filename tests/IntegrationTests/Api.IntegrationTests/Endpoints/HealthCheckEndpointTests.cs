using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Abstractions.Repositories;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the anonymous <c>GET /health/live</c> and <c>GET /health/ready</c>
/// endpoints. These must never require a token and must never leak internal details.
/// </summary>
[TestFixture]
public sealed class HealthCheckEndpointTests : IntegrationTestBase
{
    [Test]
    public async Task GetLive_WithoutToken_ReturnsOkHealthy()
    {
        // Act
        using var response = await Client.GetAsync("/api/v1/health/live");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(body);
        Assert.That(json.RootElement.GetProperty("status").GetString(), Is.EqualTo("healthy"));
    }

    [Test]
    public async Task GetReady_WithoutToken_ReturnsOkHealthyWithBothChecks()
    {
        // Act
        using var response = await Client.GetAsync("/api/v1/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("healthy"));

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        var names = checks.Select(c => c.GetProperty("name").GetString()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Is.EquivalentTo(["postgres", "migrations"]));
            Assert.That(checks.Select(c => c.GetProperty("status").GetString()), Is.All.EqualTo("healthy"));
        }
    }

    [Test]
    public async Task GetReady_ResponseShape_KeepsExactContract()
    {
        // Act
        using var response = await Client.GetAsync("/api/v1/health/ready");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var rootNames = json.RootElement.EnumerateObject().Select(p => p.Name);
        Assert.That(rootNames, Is.EquivalentTo(new[] { "status", "totalDurationMs", "checks" }));

        foreach (var check in json.RootElement.GetProperty("checks").EnumerateArray())
        {
            var checkNames = check.EnumerateObject().Select(p => p.Name);
            Assert.That(checkNames, Is.EquivalentTo(["name", "status", "durationMs", "description"]));
        }
    }

    [Test]
    public async Task GetReady_DatabaseDown_ReturnsServiceUnavailableAndUnhealthy()
    {
        // Arrange
        using var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IHealthRepository>(_ => new FakeHealthRepository(canConnect: false))));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/api/v1/health/ready");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(json.RootElement.GetProperty("status").GetString(), Is.EqualTo("unhealthy"));
        }
    }

    [Test]
    public async Task GetReady_PendingMigrations_ReturnsOkDegraded()
    {
        // Arrange
        using var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IHealthRepository>(_ => new FakeHealthRepository(canConnect: true, pendingMigrations: 2))));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/api/v1/health/ready");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(json.RootElement.GetProperty("status").GetString(), Is.EqualTo("degraded"));
        }
    }

    [TestCase("/api/v1/health/live")]
    [TestCase("/api/v1/health/ready")]
    public async Task GetHealthCheckEndpoints_WithInvalidToken_StillReturnsOkInsteadOfChallenging(string path)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateTokenWithUntrustedSignature());

        // Act
        using var response = await Client.SendAsync(request);

        // Assert: anonymous endpoints must not challenge even with a bad token.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetHealth_Authenticated_WithoutToken_StillRequiresAuthorization()
    {
        // Regression: the pre-existing authenticated GET /health must be unaffected.

        // Act
        using var response = await Client.GetAsync("/api/v1/health");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetHealthCheckEndpoints_AboveLimitWithinWindow_ReturnsTooManyRequestsProblem()
    {
        // Arrange: a fresh host gets its own limiter, isolated from the other tests in this fixture.
        using var factory = Factory.WithWebHostBuilder(_ => { });
        using var client = factory.CreateClient();

        // Act: the limit is shared by /live and /ready.
        for (var i = 0; i < 10; i++)
        {
            using var allowed = await client.GetAsync(i % 2 == 0 ? "/api/v1/health/live" : "/api/v1/health/ready");
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"request #{i + 1}");
        }

        using var rejected = await client.GetAsync("/api/v1/health/live");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(rejected.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(rejected.Headers.RetryAfter, Is.Not.Null);
        }
    }

    [Test]
    public async Task AuthenticatedHealth_IsNotAffectedByHealthRateLimit()
    {
        // Arrange
        using var factory = Factory.WithWebHostBuilder(_ => { });
        using var anonymous = factory.CreateClient();
        for (var i = 0; i < 11; i++)
        {
            using var _ = await anonymous.GetAsync("/api/v1/health/live");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken(NewFirebaseUid(), "limit@example.com"));

        // Act
        using var response = await anonymous.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private sealed class FakeHealthRepository(bool canConnect, int pendingMigrations = 0) : IHealthRepository
    {
        public Task<bool> CanConnectAsync(CancellationToken ct) => Task.FromResult(canConnect);

        public Task<int> CountPendingMigrationsAsync(CancellationToken ct) => Task.FromResult(pendingMigrations);
    }
}
