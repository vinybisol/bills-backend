using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application.DTOs.Services;
using Bogus;

namespace Api.IntegrationTests.Endpoints;

public sealed class PersonAccessLinksEndpointTests(IntegrationTestBase testBase) : IClassFixture<IntegrationTestBase>, IAsyncLifetime
{
    private const string URL = "/api/v1/persons/access-links";
    private readonly Faker _faker = new();
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
        => await testBase.ResetDatabase();

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    [Theory]
    [InlineData("", "POST")]
    [InlineData("/100/revoke", "PUT")]
    public async Task TestEndpoints_ShoulBeAutorizarion_Returns(string uri, string method)
    {
        //Arrange
        var url = string.IsNullOrWhiteSpace(uri) ? URL : $"{URL}{uri}";
        var httpMethod = new HttpMethod(method);
        var httpRequest = new HttpRequestMessage(httpMethod, url);

        //Act
        var response = await testBase.ClientWithoutToken.SendAsync(httpRequest, ct);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccessLink_PersonLessOrEqualsZero_ShouldReturnRequest()
    {
        // Arrange
        var body = new CreateAccessLinkRequest(_faker.Random.Long(long.MinValue, 0));
        HttpContent httpContent = JsonContent.Create(body);

        //Act
        var response = await testBase.Client.PostAsync(URL, httpContent, ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync(ct);
        Assert.Null(response.Headers.Location);

        Assert.Multiple(
            () => Assert.Contains("Error.Validation", responseText),
            () => Assert.Contains("less or equals zero", responseText)
        );
    }

    [Fact]
    public async Task CreateAccessLink_PersonNotExists_ShouldReturnNotFound()
    {
        // Arrange
        var body = new CreateAccessLinkRequest(_faker.Random.Long(1, long.MaxValue));
        var httpContent = JsonContent.Create(body);

        //Act
        var response = await testBase.Client.PostAsync(URL, httpContent, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync(ct);
        Assert.Null(response.Headers.Location);

        Assert.Multiple(
            () => Assert.Contains("Error.NotFound", responseText),
            () => Assert.Contains("Person não encontrado", responseText)
        );
    }

    [Fact]
    public async Task CreateAccessLink_PersonAccessLinksAlreadyExists_ShouldReturnConflict()
    {
        // Arrange
        var person = await CreatePersonAsync();
        await CreatePersonAccessLinkAsync(person);

        var body = new CreateAccessLinkRequest(person.Id);
        var httpContent = JsonContent.Create(body);

        //Act
        var response = await testBase.Client.PostAsync(URL, httpContent, ct);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync(ct);
        Assert.Null(response.Headers.Location);

        Assert.Multiple(
            () => Assert.Contains("Error.Conflict", responseText),
            () => Assert.Contains("PersonAccessLink já existe", responseText)
        );
    }

    [Fact]
    public async Task CreateAccessLink_AllSet_ShouldReturnOk()
    {
        // Arrange
        var person = await CreatePersonAsync();
        var body = new CreateAccessLinkRequest(person.Id);
        var httpContent = JsonContent.Create(body);

        //Act
        var response = await testBase.Client.PostAsync(URL, httpContent, ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseBody = await response.Content.ReadFromJsonAsync<PersonAccessLinkDto>(ct);
        Assert.NotNull(responseBody);

        Assert.Multiple(
            () => Assert.NotEqual(0, responseBody.Id),
            () => Assert.InRange(responseBody.Token.Length, 0, 100)
        );
    }

    [Fact]
    public async Task RevokeAccessLink_PersonAccessLinkNotExists_ShouldReturnNotFound()
    {
        // Arrange
        var personAccessLink = _faker.Random.Number(100, int.MaxValue);

        //Act
        var response = await testBase.Client.PutAsync($"{URL}/{personAccessLink}/revoke", null, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync(ct);
        Assert.Null(response.Headers.Location);

        Assert.Multiple(
            () => Assert.Contains("Error.NotFound", responseText),
            () => Assert.Contains("PersonAccessLink não encontrado", responseText)
        );
    }

    [Fact]
    public async Task RevokeAccessLink_AllSet_ShouldReturnOk()
    {
        // Arrange
        var person = await CreatePersonAsync();
        var personAccessLink = await CreatePersonAccessLinkAsync(person);

        //Act
        var response = await testBase.Client.PutAsync($"{URL}/{personAccessLink.Id}/revoke", null, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<PersonDto> CreatePersonAsync()
    {
        var person = new CreatePersonRequest(_faker.Name.FirstName());
        var httpContent = JsonContent.Create(person);
        var response = await testBase.Client.PostAsync("/api/v1/persons", httpContent, ct);
        var responseBody = await response.Content.ReadFromJsonAsync<PersonDto>(ct);
        Assert.NotNull(responseBody);

        return responseBody;
    }

    private async Task<PersonAccessLinkDto> CreatePersonAccessLinkAsync(PersonDto person)
    {
        var personAccessLink = new CreateAccessLinkRequest(person.Id);
        var httpContent = JsonContent.Create(personAccessLink);
        var response = await testBase.Client.PostAsync(URL, httpContent, ct);
        var responseBody = await response.Content.ReadFromJsonAsync<PersonAccessLinkDto>(ct);
        Assert.NotNull(responseBody);

        return responseBody;
    }
}