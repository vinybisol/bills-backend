using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Application.DTOs.Services;
using Bogus;
using Microsoft.AspNetCore.Http;

namespace Api.IntegrationTests.Endpoints;

public sealed class PersonEndpointTests(IntegrationTestBase testBase) : IClassFixture<IntegrationTestBase>
{
    private const string PERSONSURI = "/api/v1/persons";
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

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
        var person = new Faker<CreatePersonRequest>().CustomInstantiator(f => new CreatePersonRequest(f.Name.FirstName())).Generate();
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
}