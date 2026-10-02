using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Contracts;
using Api.IntegrationTests.TestSupport;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the RFC 9457 error contract: every error response — from services,
/// the framework (auth, routing, binding) or unhandled exceptions — is an
/// <c>application/problem+json</c> body with <c>type/title/status/instance/traceId</c>.
/// The factory runs in the <c>Testing</c> environment, so the exception handler is active.
/// </summary>
[TestFixture]
public sealed class ProblemDetailsTests : IntegrationTestBase
{
    private const string AccessLinksUri = "/api/v1/persons/access-links";
    private const string SharedUri = "/api/v1/bills/shared";

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(ProblemAssertions.ProblemJson));

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo((int)expectedStatus));
            Assert.That(problem.GetProperty("title").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(problem.GetProperty("type").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(problem.GetProperty("traceId").GetString(), Is.Not.Null.And.Not.Empty);
        }
        return problem;
    }

    // --- Framework-generated errors (status code pages) ---

    [Test]
    public async Task ProtectedEndpoint_WithoutToken_Returns401ProblemDetails()
    {
        // Act
        using var response = await Client.GetAsync("/api/v1/categories");

        // Assert
        var problem = await ReadProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.That(problem.GetProperty("instance").GetString(), Is.EqualTo("/api/v1/categories"));
    }

    [Test]
    public async Task ProtectedEndpoint_WithUntrustedToken_Returns401ProblemDetails()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateTokenWithUntrustedSignature());

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        await ReadProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        // Act
        using var response = await Client.GetAsync("/api/v1/does-not-exist");

        // Assert
        var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.That(problem.GetProperty("instance").GetString(), Is.EqualTo("/api/v1/does-not-exist"));
    }

    [Test]
    public async Task WrongHttpMethod_Returns405ProblemDetails()
    {
        // Act — /bills/shared only maps GET
        using var response = await Client.DeleteAsync(SharedUri);

        // Assert
        await ReadProblemAsync(response, HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task MalformedJsonBody_Returns400ProblemDetails()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        using var content = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");

        // Act
        using var response = await client.PostAsync("/api/v1/categories", content);

        // Assert
        await ReadProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownEnumValue_Returns400ProblemDetails()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act — "kind" is bound to an enum; an unknown string fails JSON binding
        using var response = await client.PostAsJsonAsync("/api/v1/incomes", new { name = "Salário", kind = "weekly", defaultAmount = 10m });

        // Assert
        await ReadProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnsupportedContentType_Returns415ProblemDetails()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        using var content = new StringContent("name=x", Encoding.UTF8, "text/plain");

        // Act
        using var response = await client.PostAsync("/api/v1/categories", content);

        // Assert
        await ReadProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    // --- Service-level errors (Result → ProblemDetails) ---

    [Test]
    public async Task ServiceNotFound_ReturnsProblemWithCodeDetailInstanceAndTraceId()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var uri = $"{AccessLinksUri}/{int.MaxValue}/revoke";

        // Act
        using var response = await client.PutAsync(uri, null);

        // Assert
        var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("Error.NotFound"));
            Assert.That(problem.GetProperty("title").GetString(), Is.Not.EqualTo("Error.NotFound"));
            Assert.That(problem.GetProperty("detail").GetString(), Does.Contain("PersonAccessLink não encontrado"));
            Assert.That(problem.GetProperty("instance").GetString(), Is.EqualTo(uri));
        }
    }

    [Test]
    public async Task ServiceConflict_ReturnsProblemWithCodeInstanceAndTraceId()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        using var personResponse = await client.PostAsJsonAsync(
            "/api/v1/persons", new CreatePersonRequest($"Person {Guid.NewGuid():N}"));
        var person = await personResponse.Content.ReadFromJsonAsync<PersonDto>();
        using var first = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(person!.Id));
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var response = await client.PostAsJsonAsync(AccessLinksUri, new CreateAccessLinkRequest(person.Id));

        // Assert
        var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("Error.Conflict"));
            Assert.That(problem.GetProperty("instance").GetString(), Is.EqualTo(AccessLinksUri));
        }
    }

    [Test]
    public async Task ServiceValidation_ReturnsValidationProblemWithCodeAndErrors()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest("   "));

        // Assert
        var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("Error.Validation"));
            Assert.That(problem.GetProperty("errors").TryGetProperty("name", out _), Is.True);
        }
    }

    // --- Shared token ---

    [TestCase("")]
    [TestCase("?token=")]
    [TestCase("?token=%20%20")]
    public async Task SharedBills_MissingToken_ReturnsValidationProblemOnToken(string query)
    {
        // Act — anonymous endpoint
        using var response = await Client.GetAsync($"{SharedUri}{query}");

        // Assert
        await ProblemAssertions.AssertValidationProblemAsync(response, "token");
    }

    [TestCase("not-a-token")]
    [TestCase("eyJQZXJzb25JZCI6MSwiVG9rZW4iOiJYIn0")] // base64url of {"PersonId":1,"Token":"X"} — well-formed, unknown
    public async Task SharedBills_InvalidOrUnknownToken_ReturnsOkFalse(string token)
    {
        // Act
        using var response = await Client.GetAsync($"{SharedUri}?token={token}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadFromJsonAsync<bool>(), Is.False);
    }

    // --- Unhandled exceptions (exception handler, outside Development) ---

    [Test]
    public async Task UnhandledException_Returns500ProblemDetailsWithoutStackTrace()
    {
        // Arrange — same host, but the shared-link service blows up with an unexpected exception
        await using var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IPersonAccessLinksService, ThrowingPersonAccessLinksService>()));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"{SharedUri}?token=anything");

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        var problem = await ReadProblemAsync(response, HttpStatusCode.InternalServerError);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(problem.GetProperty("instance").GetString(), Is.EqualTo(SharedUri));
            Assert.That(body, Does.Not.Contain(ThrowingPersonAccessLinksService.SecretMessage));
            Assert.That(body, Does.Not.Contain(nameof(ThrowingPersonAccessLinksService)));
            Assert.That(body, Does.Not.Contain("   at "), "No stack trace in the response");
            Assert.That(problem.TryGetProperty("exception", out _), Is.False);
        }
    }

    private sealed class ThrowingPersonAccessLinksService : IPersonAccessLinksService
    {
        public const string SecretMessage = "boom: internal detail that must not leak";

        public Task<Result> ValidateTokenAsync(string? token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(SecretMessage);

        public Task<Result<PersonAccessLinkDto>> CreateAsync(long personId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(SecretMessage);

        public Task<Result> RevokeAsync(long id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(SecretMessage);
    }
}
