using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application.DTOs.Services;
using Application.DTOs.Services.PesonAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the <c>/api/v1/persons/access-links</c> endpoints. Each test
/// authenticates as a fresh Firebase uid, so its persons/links are isolated by the owner filter.
/// </summary>
[TestFixture]
public sealed class PersonAccessLinksEndpointTests : IntegrationTestBase
{
    private const string AccessLinksUri = "/api/v1/persons/access-links";
    private static readonly DateTimeOffset _expiresAt = new(2026, 9, 8, 9, 35, 0, TimeSpan.Zero);


    [TestCase("", "POST")]
    [TestCase("", "GET")]
    [TestCase("/100/revoke", "PUT")]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized(string uri, string method)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{AccessLinksUri}{uri}");

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(0L)]
    [TestCase(long.MinValue)]
    public async Task CreateAccessLink_PersonIdLessOrEqualsZero_ReturnsBadRequest(long personId)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(personId, _expiresAt));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var responseText = await response.Content.ReadAsStringAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(responseText, Does.Contain("Error.Validation"));
            Assert.That(responseText, Does.Contain("less or equals zero"));
        }
    }

    [Test]
    public async Task CreateAccessLink_PersonNotExists_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(long.MaxValue, _expiresAt));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(responseText, Does.Contain("Error.NotFound"));
            Assert.That(responseText, Does.Contain("Person não encontrado"));
        });
    }

    [Test]
    public async Task CreateAccessLink_DateAlreadyExpired_ReturnValidationError()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(person.Id, _expiresAt));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var responseText = await response.Content.ReadAsStringAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(responseText, Does.Contain("Error.Validation"));
            Assert.That(responseText, Does.Contain("Token with worng expire time"));
        }
    }

    [Test]
    public async Task CreateAccessLink_ValidPerson_ReturnsOkWithToken()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);

        var timeProvider = Factory.Services.GetRequiredService<TimeProvider>();
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(10);

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(person.Id, expiresAt));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var responseBody = await response.Content.ReadFromJsonAsync<CreatePesonAccessLinkDto>();
        Assert.That(responseBody, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location, Is.Null);
            var tokenSplited = responseBody.Token.Split('.');
            Assert.That(responseBody.Token, Has.Length.AtLeast(20));
            Assert.That(tokenSplited, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task RevokeAccessLink_AccessLinkNotExists_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PutAsync($"{AccessLinksUri}/{int.MaxValue}/revoke", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var responseText = await response.Content.ReadAsStringAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(responseText, Does.Contain("Error.NotFound"));
            Assert.That(responseText, Does.Contain("PersonAccessLink não encontrado"));
        }
    }

    [Test]
    public async Task RevokeAccessLink_ExistingAccessLink_ReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);
        Assert.That(person, Is.Not.Null);
        var timeProvider = Factory.Services.GetRequiredService<TimeProvider>();
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(10);
        var pesonAccessLinkDto = await CreateAccessLinkAsync(client, person.Id, expiresAt);

        // Act
        using var response = await client.PutAsync($"{AccessLinksUri}/{pesonAccessLinkDto.Id}/revoke", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task GetAllAsync_NotExistingAccessLink_ReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync($"{AccessLinksUri}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task GetAllAsync_ExistingTwoAccessLink_ReturnsOk()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);
        Assert.That(person, Is.Not.Null);
        var timeProvider = Factory.Services.GetRequiredService<TimeProvider>();
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(10);
        _ = await CreateAccessLinkAsync(client, person.Id, expiresAt);
        _ = await CreateAccessLinkAsync(client, person.Id, expiresAt);

        // Act
        using var response = await client.GetAsync($"{AccessLinksUri}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<IEnumerable<PesonAccessLinkDto>>();
        Assert.That(body, Is.Not.Null);
        var links = body.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(links, Has.Count.EqualTo(2));
            Assert.That(links, Has.All.Matches<PesonAccessLinkDto>(link => link!.PersonId == person.Id));
        }
    }

    private static async Task<PersonDto> CreatePersonAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/persons", new CreatePersonRequest($"Person {Guid.NewGuid():N}"));
        var body = await response.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(body, Is.Not.Null);
        return body!;
    }

    private static async Task<CreatePesonAccessLinkDto> CreateAccessLinkAsync(HttpClient client, long personId, DateTimeOffset expiresAt)
    {
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(personId, expiresAt));
        var body = await response.Content.ReadFromJsonAsync<CreatePesonAccessLinkDto>();
        Assert.That(body, Is.Not.Null);
        return body;
    }
}