using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the authenticated <c>GET /me</c> endpoint, exercising the full
/// pipeline: JWT validation, just-in-time provisioning and the JSON response shape.
/// </summary>
[TestFixture]
public sealed class MeEndpointTests : IntegrationTestBase
{
    [Test]
    public async Task GetMe_WithValidToken_ReturnsOkWithIdNameAndEmail()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken("firebase-me-1", "me@example.com", "Me User"));

        // Act
        using var response = await Client.SendAsync(request);

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
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken("firebase-me-2", "noname@example.com", name: null));

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<MeDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task GetMe_TokenWithNoEmailClaim_ReturnsOkWithNullEmail()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken(NewFirebaseUid(), email: null));

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<MeDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Email, Is.Null);
    }

    [Test]
    public async Task GetMe_ReturnsSameIdAsHealthForSameToken()
    {
        // Arrange
        var token = TestTokens.CreateValidToken(NewFirebaseUid());
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var healthRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        healthRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        using var meResponse = await Client.SendAsync(meRequest);
        using var healthResponse = await Client.SendAsync(healthRequest);

        // Assert
        var me = await meResponse.Content.ReadFromJsonAsync<MeDto>();
        var health = await healthResponse.Content.ReadFromJsonAsync<HealthDto>();
        Assert.That(me!.Id, Is.EqualTo(health!.UserId));
    }

    [Test]
    public async Task GetMe_FirstAccess_ProvisionsUserWithDefaultCategories()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var meResponse = await client.GetAsync("/api/v1/me");
        var categories = await client.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories");

        // Assert
        Assert.That(meResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(categories!.Select(c => c.Name), Is.EquivalentTo(Domain.Entities.Category.DefaultNames));
    }

    [Test]
    public async Task GetMe_WithUntrustedSignature_ReturnsUnauthorized()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateTokenWithUntrustedSignature());

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetMe_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange / Act
        using var response = await Client.GetAsync("/api/v1/me");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private sealed record MeDto(long Id, string Name, string? Email);

    private sealed record HealthDto(long UserId, string Status);

    private sealed record CategoryDto(long Id, string Name);
}
