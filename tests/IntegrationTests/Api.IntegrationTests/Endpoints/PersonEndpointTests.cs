using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application.DTOs.Services;
using Bogus;
using IntegrationCommon.TestData;
using Microsoft.AspNetCore.Http;

namespace Api.IntegrationTests.Endpoints;

public sealed class PersonEndpointTests(IntegrationTestBase testBase) : IClassFixture<IntegrationTestBase>, IAsyncLifetime
{
    private const string PERSONSURI = "/api/v1/persons";
    private readonly Faker _faker = new();
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;


    public async ValueTask InitializeAsync()
    {
        await testBase.ResetDatabase();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("", "GET")]
    [InlineData("", "POST")]
    [InlineData("/100", "PUT")]
    [InlineData("/100", "DELETE")]
    public async Task TestEndpoints_ShoulBeAutorizarion_Returns(string uri, string method)
    {
        //Arrange
        var url = string.IsNullOrWhiteSpace(uri) ? PERSONSURI : $"{PERSONSURI}{uri}";
        var httpMethod = new HttpMethod(method);
        var httpRequest = new HttpRequestMessage(httpMethod, url);

        //Act
        var response = await testBase.ClientWithoutToken.SendAsync(httpRequest, ct);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

    }

    [Fact]
    public async Task CreatePerson_WithValidToken_ReturnsCreatedWithDto()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var person = new CreatePersonRequest(new Faker().Name.FirstName());
        HttpContent httpContent = JsonContent.Create(person);

        //Act
        var response = await testBase.Client.PostAsync(PERSONSURI, httpContent, ct);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(body);
        Assert.NotNull(response.Headers.Location);

        Assert.Multiple(
            () => Assert.True(body.Id > 0, $"Expected Id greater than 0, but was {body.Id}"),
            () => Assert.Equal(person.Name, body.Name),
            () => Assert.Contains($"/persons/{body.Id}", response.Headers.Location!.ToString())
        );
    }

    [Theory]
    [ClassData<InvalidStrings>]
    public async Task CreatePerson_WithInvalidData_ReturnsBadRequest(string invalidStrings)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var person = new CreatePersonRequest(invalidStrings);
        HttpContent httpContent = JsonContent.Create(person);

        //Act
        var response = await testBase.Client.PostAsync(PERSONSURI, httpContent, ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.NotNull(body);
        Assert.Multiple(
            () => Assert.Null(response.Headers.Location),
            () => Assert.Contains("Person name cannot be empty ou null", body)
        );
    }

    [Fact]
    public async Task CreatePerson_NameAlreadyExists_ReturnsBadRequest()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var name = _faker.Name.FirstName();
        await CreateDummyPerson(name);
        var person = new CreatePersonRequest(name);
        HttpContent httpContent = JsonContent.Create(person);

        //Act
        var response = await testBase.Client.PostAsync(PERSONSURI, httpContent, ct);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.NotNull(body);
        Assert.Multiple(
            () => Assert.Null(response.Headers.Location),
            () => Assert.Contains("A person with that name already exists", body)
        );
    }

    [Fact]
    public async Task ListPersons_OnePerson_ReturnsPerson()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var name = _faker.Name.FindName();
        await CreateDummyPerson(name);

        //Act
        var response = await testBase.Client.GetAsync(PERSONSURI, ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PersonDto[]>(ct);
        Assert.NotNull(body);
        Assert.Multiple(
            () => Assert.Null(response.Headers.Location),
            () => Assert.Single(body),
            () => Assert.Equal(name, body[0].Name)
        );
    }

    [Fact]
    public async Task ListPersons_NewUser_ReturnsEmptyList()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uid = "new-user-id";
        await CreateDummyPerson(uid: uid);

        //Act
        var response = await testBase.Client.GetAsync(PERSONSURI, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Multiple(
            () => Assert.Empty(body),
            () => Assert.Null(response.Headers.Location)
        );
    }

    [Fact]
    public async Task UpdatePerson_Rename_ReturnsOkWithUpdatedName()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var name = _faker.Name.FindName();
        var response = await CreateDummyPerson(name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(body);
        Assert.Equal(name, body.Name);

        var updateName = _faker.Name.FindName();
        var updateReq = JsonContent.Create(new UpdatePersonRequest(updateName));

        //Act
        var updateResponse = await testBase.Client.PutAsync($"{PERSONSURI}/{body.Id}", updateReq, ct);

        // Assert
        var updateBody = await updateResponse.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(updateBody);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode),
            () => Assert.Null(updateResponse.Headers.Location),
            () => Assert.Equal(updateName, updateBody.Name)
        );
    }

    [Theory]
    [ClassData<InvalidStrings>]
    public async Task UpdatePerson_WithInvalidData_ReturnsBadRequest(string invalidStrings)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var name = _faker.Name.FindName();
        var response = await CreateDummyPerson(name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(body);
        Assert.Equal(name, body.Name);

        var updateReq = JsonContent.Create(new UpdatePersonRequest(invalidStrings));

        //Act
        var updateResponse = await testBase.Client.PutAsync($"{PERSONSURI}/{body.Id}", updateReq, ct);

        // Assert
        var updateBody = await updateResponse.Content.ReadAsStringAsync(ct);
        Assert.NotNull(updateBody);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode),
            () => Assert.Null(updateResponse.Headers.Location),
            () => Assert.Contains("Person name cannot be empty ou null", updateBody)
        );
    }

    [Fact]
    public async Task UpdatePerson_NotFound_ReturnsNotFound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var name = _faker.Name.FindName();
        var req = JsonContent.Create(new UpdatePersonRequest(name));

        //Act
        var response = await testBase.Client.PutAsync($"{PERSONSURI}/{_faker.Database.Random.Int()}", req, ct);

        // Assert
        var updateBody = await response.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(updateBody);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode),
            () => Assert.Null(response.Headers.Location)
        );
    }

    [Fact]
    public async Task UpdatePerson_AlreadyExists_ReturnsConflict()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var name = _faker.Name.FindName();
        var response = await CreateDummyPerson(name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(body);
        Assert.Equal(name, body.Name);

        var updateReq = JsonContent.Create(new UpdatePersonRequest(name));

        //Act
        var updateResponse = await testBase.Client.PutAsync($"{PERSONSURI}/{body.Id}", updateReq, ct);

        // Assert
        var updateBody = await updateResponse.Content.ReadAsStringAsync(ct);
        Assert.NotNull(updateBody);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode),
            () => Assert.Null(updateResponse.Headers.Location),
            () => Assert.Contains("A person with that name already exists.", updateBody)
        );
    }

    [Fact]
    public async Task DeletePerson_NotFound_ReturnsNotFound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;

        //Act
        var response = await testBase.Client.DeleteAsync($"{PERSONSURI}/{_faker.Database.Random.Int()}", ct);

        // Assert
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.NotNull(body);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode),
            () => Assert.Null(response.Headers.Location)
        );
    }

    [Fact]
    public async Task DeletePerson_DeactivatedPerson_DisappearsFromList()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var name = _faker.Name.FindName();
        var response = await CreateDummyPerson(name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(body);
        Assert.Equal(name, body.Name);

        //Act
        var deleteResponse = await testBase.Client.DeleteAsync($"{PERSONSURI}/{body.Id}", ct);
        var listResponse = await testBase.Client.GetAsync(PERSONSURI, ct);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode),
            () => Assert.Equal(HttpStatusCode.NoContent, listResponse.StatusCode),
            () => Assert.Null(deleteResponse.Headers.Location)
        );
    }

    private async Task<HttpResponseMessage> CreateDummyPerson(string? name = null, string? uid = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var nameOfPerson = name ?? _faker.Name.FirstName();
        var person = new CreatePersonRequest(nameOfPerson);
        HttpContent httpContent = JsonContent.Create(person);
        if (string.IsNullOrWhiteSpace(uid))
            return await testBase.Client.PostAsync(PERSONSURI, httpContent, ct);
        else
            return await testBase.ClientWithUid(uid).PostAsync(PERSONSURI, httpContent, ct);
    }
}