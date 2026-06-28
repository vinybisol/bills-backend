using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the authenticated <c>GET /health</c> endpoint, exercising the
/// full pipeline: JWT validation, just-in-time provisioning and the JSON response.
/// </summary>
[TestFixture]
public class HealthEndpointTests
{
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetHealth_WithValidToken_ReturnsOkAndInternalUserId()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken("firebase-alice"));

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.UserId, Is.GreaterThan(0));
        Assert.That(body.Status, Is.EqualTo("healthy"));
    }

    [Test]
    public async Task GetHealth_WithSameTokenTwice_ProvisionsUserOnceAndReturnsSameId()
    {
        var token = TestTokens.CreateValidToken("firebase-repeat");

        var firstId = await GetUserIdAsync(token);
        var secondId = await GetUserIdAsync(token);

        Assert.That(secondId, Is.EqualTo(firstId));
    }

    [Test]
    public async Task GetHealth_WithUntrustedSignature_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateTokenWithUntrustedSignature());

        using var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetHealth_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<long> GetUserIdAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        Assert.That(body, Is.Not.Null);
        return body!.UserId;
    }

    private sealed record HealthDto(long UserId, string Status);
}
