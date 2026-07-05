using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the authenticated <c>GET /health</c> endpoint, exercising the
/// full pipeline: JWT validation, just-in-time provisioning and the JSON response.
/// </summary>
[TestFixture]
public sealed class HealthEndpointTests : IntegrationTestBase
{
    [Test]
    public async Task GetHealth_WithValidToken_ReturnsOkAndInternalUserId()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken("firebase-alice", "alice@example.com"));

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.UserId, Is.GreaterThan(0));
        Assert.That(body.Status, Is.EqualTo("healthy"));
    }

    [Test]
    public async Task GetHealth_WithSameTokenTwice_ProvisionsUserOnceAndReturnsSameId()
    {
        // Arrange
        var token = TestTokens.CreateValidToken("firebase-repeat", "repeat@example.com");

        // Act
        var firstId = await GetUserIdAsync(token);
        var secondId = await GetUserIdAsync(token);

        // Assert
        Assert.That(secondId, Is.EqualTo(firstId));
    }

    [Test]
    public async Task GetHealth_WithUntrustedSignature_ReturnsUnauthorized()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateTokenWithUntrustedSignature());

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetHealth_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange / Act
        using var response = await Client.GetAsync("/api/v1/health");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<long> GetUserIdAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await Client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        Assert.That(body, Is.Not.Null);
        return body!.UserId;
    }

    private sealed record HealthDto(long UserId, string Status);
}
