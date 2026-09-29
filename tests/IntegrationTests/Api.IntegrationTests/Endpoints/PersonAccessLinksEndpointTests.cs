using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application.DTOs.Services;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the <c>/api/v1/persons/access-links</c> endpoints. Each test
/// authenticates as a fresh Firebase uid, so its persons/links are isolated by the owner filter.
/// </summary>
[TestFixture]
public sealed class PersonAccessLinksEndpointTests : IntegrationTestBase
{
    private const string AccessLinksUri = "/api/v1/persons/access-links";

    [TestCase("", "POST")]
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
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(personId));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(responseText, Does.Contain("Error.Validation"));
            Assert.That(responseText, Does.Contain("less or equals zero"));
        });
    }

    [Test]
    public async Task CreateAccessLink_PersonNotExists_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(long.MaxValue));

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
    public async Task CreateAccessLink_AccessLinkAlreadyExists_ReturnsConflict()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);
        await CreateAccessLinkAsync(client, person);

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(person.Id));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(responseText, Does.Contain("Error.Conflict"));
            Assert.That(responseText, Does.Contain("PersonAccessLink já existe"));
        });
    }

    [Test]
    public async Task CreateAccessLink_ValidPerson_ReturnsOkWithToken()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(person.Id));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<PersonAccessLinkDto>();
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Id, Is.Not.Zero);
            Assert.That(body.Token, Has.Length.GreaterThan(20));
        });
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
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(responseText, Does.Contain("Error.NotFound"));
            Assert.That(responseText, Does.Contain("PersonAccessLink não encontrado"));
        });
    }

    [Test]
    public async Task RevokeAccessLink_ExistingAccessLink_ReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);
        var accessLink = await CreateAccessLinkAsync(client, person);

        // Act
        using var response = await client.PutAsync($"{AccessLinksUri}/{accessLink.Id}/revoke", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    private static async Task<PersonDto> CreatePersonAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/persons", new CreatePersonRequest($"Person {Guid.NewGuid():N}"));
        var body = await response.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(body, Is.Not.Null);
        return body!;
    }

    private static async Task<PersonAccessLinkDto> CreateAccessLinkAsync(HttpClient client, PersonDto person)
    {
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(person.Id));
        var body = await response.Content.ReadFromJsonAsync<PersonAccessLinkDto>();
        Assert.That(body, Is.Not.Null);
        return body!;
    }
}