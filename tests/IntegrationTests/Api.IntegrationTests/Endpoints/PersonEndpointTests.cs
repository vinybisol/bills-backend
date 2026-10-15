using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Api.IntegrationTests.TestSupport;
using Application.DTOs.Services;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the <c>/api/v1/persons</c> endpoints. Each test authenticates as a
/// fresh Firebase uid, so its persons are isolated by the owner filter.
/// </summary>
[TestFixture]
public sealed class PersonEndpointTests : IntegrationTestBase
{
    private const string PersonsUri = "/api/v1/persons";

    [TestCase("", "GET")]
    [TestCase("", "POST")]
    [TestCase("/100", "PUT")]
    [TestCase("/100", "DELETE")]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized(string uri, string method)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{PersonsUri}{uri}");

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task CreatePerson_WithValidToken_ReturnsCreatedWithDto()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var person = new CreatePersonRequest(NewName());

        // Act
        using var response = await client.PostAsJsonAsync(PersonsUri, person);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var body = await response.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(response.Headers.Location, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Id, Is.GreaterThan(0));
            Assert.That(body.Name, Is.EqualTo(person.Name));
            Assert.That(response.Headers.Location!.ToString(), Does.Contain($"/persons/{body.Id}"));
        });
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task CreatePerson_WithInvalidName_ReturnsBadRequest(string? invalidName)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(PersonsUri, new CreatePersonRequest(invalidName!));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(body, Does.Contain("Person name cannot be empty or null"));
        });
    }

    [Test]
    public async Task CreatePerson_NameAlreadyExists_ReturnsConflict()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var name = NewName();
        await CreatePersonAsync(client, name);

        // Act
        using var response = await client.PostAsJsonAsync(PersonsUri, new CreatePersonRequest(name));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(body, Does.Contain("A person with that name already exists"));
        });
    }

    [Test]
    public async Task ListPersons_OnePerson_ReturnsPerson()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var name = NewName();
        await CreatePersonAsync(client, name);

        // Act
        using var response = await client.GetAsync(PersonsUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<PersonDto[]>();
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(body, Has.Length.EqualTo(1));
            Assert.That(body![0].Name, Is.EqualTo(name));
        });
    }

    [Test]
    public async Task ListPersons_NewUser_ReturnsNoContent()
    {
        // Arrange — another user owns a person; the new user must not see it.
        using (var otherClient = CreateAuthenticatedClient())
        {
            await CreatePersonAsync(otherClient, NewName());
        }

        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync(PersonsUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(body, Is.Empty);
            Assert.That(response.Headers.Location, Is.Null);
        });
    }

    [Test]
    public async Task UpdatePerson_Rename_ReturnsOkWithUpdatedName()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreatePersonAsync(client, NewName());
        var newName = NewName();

        // Act
        using var response = await client.PutAsJsonAsync($"{PersonsUri}/{created.Id}", new UpdatePersonRequest(newName));

        // Assert
        var body = await response.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(body!.Name, Is.EqualTo(newName));
        });
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task UpdatePerson_WithInvalidName_ReturnsBadRequest(string? invalidName)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreatePersonAsync(client, NewName());

        // Act
        using var response = await client.PutAsJsonAsync($"{PersonsUri}/{created.Id}", new UpdatePersonRequest(invalidName!));

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(body, Does.Contain("Person name cannot be empty ou null"));
        });
    }

    [Test]
    public async Task UpdatePerson_NotFound_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PutAsJsonAsync($"{PersonsUri}/{int.MaxValue}", new UpdatePersonRequest(NewName()));

        // Assert
        var body = await response.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(response.Headers.Location, Is.Null);
        });
    }

    [Test]
    public async Task UpdatePerson_NameAlreadyExists_ReturnsConflict()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var name = NewName();
        var created = await CreatePersonAsync(client, name);

        // Act
        using var response = await client.PutAsJsonAsync($"{PersonsUri}/{created.Id}", new UpdatePersonRequest(name));

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(body, Does.Contain("A person with that name already exists."));
        });
    }

    [Test]
    public async Task DeletePerson_NotFound_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.DeleteAsync($"{PersonsUri}/{int.MaxValue}");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(response.Headers.Location, Is.Null);
        });
    }

    [Test]
    public async Task DeletePerson_DeactivatedPerson_DisappearsFromList()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreatePersonAsync(client, NewName());

        // Act
        using var deleteResponse = await client.DeleteAsync($"{PersonsUri}/{created.Id}");
        using var listResponse = await client.GetAsync(PersonsUri);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(deleteResponse.Headers.Location, Is.Null);
        });
    }

    private static string NewName() => $"Person {Guid.NewGuid():N}";

    private static async Task<PersonDto> CreatePersonAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(PersonsUri, new CreatePersonRequest(name));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var body = await response.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Name, Is.EqualTo(name));
        return body;
    }
}