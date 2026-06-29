using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the authenticated <c>GET /me</c> endpoint, exercising the full
/// pipeline: JWT validation, just-in-time provisioning and the JSON response shape.
/// </summary>
[TestFixture]
public sealed class MeEndpointTests
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
    public async Task GetMe_WithValidToken_ReturnsOkWithIdNameAndEmail()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken("firebase-me-1", "me@example.com", "Me User"));

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<MeDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Id, Is.GreaterThan(0));
        Assert.That(body.Name, Is.EqualTo("Me User"));
        Assert.That(body.Email, Is.EqualTo("me@example.com"));
    }

    [Test]
    public async Task GetMe_TokenWithNoNameClaim_ReturnsOkWithEmptyName()
    {
        // Arrange — name: null omits the claim from the token
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken("firebase-me-2", "noname@example.com", name: null));

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<MeDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task GetMe_WithUntrustedSignature_ReturnsUnauthorized()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateTokenWithUntrustedSignature());

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetMe_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange / Act
        using var response = await _client.GetAsync("/me");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private sealed record MeDto(long Id, string Name, string? Email);
}
