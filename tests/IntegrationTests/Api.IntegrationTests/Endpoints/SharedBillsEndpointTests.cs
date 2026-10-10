using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Api.IntegrationTests.TestSupport;
using Application.DTOs.Services;
using Application.DTOs.Services.PesonAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests.Endpoints;

[TestFixture]
public sealed class SharedBillsEndpointTests : IntegrationTestBase
{
    private const string URL = "/api/v1/bills/shared";

    [SetUp]
    public void Setup()
    {
    }

    [TestCase("POST")]
    [TestCase("PATCH")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task GetSharedBills_WithoutToken_ReturnUnauthorized(string method)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{URL}");

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task GetSharedBills_WithoutTokenOnParameters_ReturnUnauthorized(string query)
    {
        // Arrange                
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync($"{URL}{query}");

        // Assert
        await ProblemAssertions.AssertValidationProblemAsync(response, "Error.Validation");
    }

    [Test]
    public async Task GetSharedBills_WithValidToken_ReturnSharedBills()
    {
        // Arrange               
        using var client = CreateAuthenticatedClient();
        var person = await CreatePersonAsync(client);
        var timeProvider = Factory.Services.GetRequiredService<TimeProvider>();
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(10);
        using var tokenRes = await client.PostAsJsonAsync("/api/v1/persons/access-links", new CreateAccessLinkRequest(person.Id, expiresAt));
        Assert.That(tokenRes.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var tokenBody = await tokenRes.Content.ReadFromJsonAsync<CreatePesonAccessLinkDto>();
        Assert.That(tokenBody, Is.Not.Null);

        // Act
        using var response = await client.GetAsync($"{URL}?token={tokenBody.Token}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bills = await response.Content.ReadFromJsonAsync<ReceivablesMonthDto>();
        Assert.That(bills, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bills.ByPerson.ToList(), Has.Count.AtLeast(1));
            var year = timeProvider.GetUtcNow().Year;
            Assert.That(bills.Year, Is.EqualTo(year));
            var month = timeProvider.GetUtcNow().Month;
            Assert.That(bills.Month, Is.EqualTo(month));
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
}
