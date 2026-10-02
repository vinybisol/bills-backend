using System.Net;
using System.Net.Http.Json;
using Api.IntegrationTests.TestSupport;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the <c>/api/v1/incomes</c> endpoints, covering the full request
/// pipeline: JWT validation, validation problems, owner isolation, and soft delete. Each test
/// authenticates as a fresh Firebase uid, so its incomes are isolated by the owner filter.
/// </summary>
[TestFixture]
public sealed class IncomeEndpointTests : IntegrationTestBase
{
    private const string IncomesUri = "/api/v1/incomes";

    [TestCase("", "GET")]
    [TestCase("", "POST")]
    [TestCase("/100", "PUT")]
    [TestCase("/100", "DELETE")]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized(string uri, string method)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{IncomesUri}{uri}");
        if (method is "POST" or "PUT")
            request.Content = JsonContent.Create(new { name = "Salário", kind = "recurring", defaultAmount = 5000m });

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Create ---

    [Test]
    public async Task CreateIncome_WithValidToken_ReturnsCreatedWithDtoAndLocation()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(IncomesUri,
            new { name = "Salário", kind = "recurring", defaultAmount = 5000m });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto>();
        Assert.That(body, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Id, Is.GreaterThan(0));
            Assert.That(body.Name, Is.EqualTo("Salário"));
            Assert.That(body.Kind, Is.EqualTo("recurring"));
            Assert.That(body.DefaultAmount, Is.EqualTo(5000m));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"{IncomesUri}/{body.Id}"));
        }
    }

    [Test]
    public async Task CreateIncome_NameWithSurroundingWhitespace_PersistsTrimmedName()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        var created = await CreateIncomeAsync(client, "  Freela  ", "one_off", 0m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Name, Is.EqualTo("Freela"));
            Assert.That(created.Kind, Is.EqualTo("one_off"));
            Assert.That(created.DefaultAmount, Is.Zero);
        }
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task CreateIncome_BlankName_ReturnsValidationProblem(string? name)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(IncomesUri,
            new { name, kind = "recurring", defaultAmount = 1000m });

        // Assert
        await AssertValidationProblemAsync(response, "name");
    }

    [TestCase("salary")]
    [TestCase("monthly")]
    [TestCase("")]
    public async Task CreateIncome_UnknownKindString_ReturnsBadRequest(string kind)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(IncomesUri,
            new { name = "Renda", kind, defaultAmount = 1000m });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(response.Headers.Location, Is.Null);
        }
    }

    [Test]
    public async Task CreateIncome_UndefinedNumericKind_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(IncomesUri,
            new { name = "Renda", kind = 5, defaultAmount = 1000m });

        // Assert
        await AssertValidationProblemAsync(response, "kind");
    }

    [Test]
    public async Task CreateIncome_NegativeDefaultAmount_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(IncomesUri,
            new { name = "Renda", kind = "recurring", defaultAmount = -1m });

        // Assert
        await AssertValidationProblemAsync(response, "defaultAmount");
    }

    [Test]
    public async Task CreateIncome_AllFieldsInvalid_ReturnsOneErrorPerField()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(IncomesUri,
            new { name = " ", kind = 9, defaultAmount = -1m });

        // Assert
        await AssertValidationProblemAsync(response, "name", "kind", "defaultAmount");
    }

    [Test]
    public async Task CreateIncome_Invalid_DoesNotPersistAnything()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        using var createResp = await client.PostAsJsonAsync(IncomesUri,
            new { name = "Renda", kind = "recurring", defaultAmount = -1m });
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // Act
        using var listResp = await client.GetAsync(IncomesUri);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    // --- List ---

    [Test]
    public async Task ListIncomes_NewUser_ReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync(IncomesUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task ListIncomes_AfterCreating_IncludesNewIncome()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Salário", "recurring", 5000m);

        // Act
        using var response = await client.GetAsync(IncomesUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(body, Is.EqualTo(new[] { created }));
    }

    [Test]
    public async Task ListIncomes_MultipleIncomes_ReturnsOrderedByName()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        await CreateIncomeAsync(client, "Salário", "recurring", 5000m);
        await CreateIncomeAsync(client, "Aluguel recebido", "recurring", 1200m);
        await CreateIncomeAsync(client, "Freela", "one_off", 800m);

        // Act
        using var response = await client.GetAsync(IncomesUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(body!.Select(i => i.Name), Is.EqualTo(new[] { "Aluguel recebido", "Freela", "Salário" }));
    }

    // --- Update ---

    [Test]
    public async Task UpdateIncome_Valid_ReturnsOkWithUpdatedDto()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Salário", "recurring", 5000m);

        // Act
        using var response = await client.PutAsJsonAsync($"{IncomesUri}/{created.Id}",
            new { name = "  Freelance  ", kind = "one_off", defaultAmount = 2500m });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto>();
        Assert.That(body, Is.EqualTo(new IncomeDto(created.Id, "Freelance", "one_off", 2500m)));

        using var listResp = await client.GetAsync(IncomesUri);
        var list = await listResp.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(list, Is.EqualTo(new[] { body }));
    }

    [Test]
    public async Task UpdateIncome_NotFound_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PutAsJsonAsync($"{IncomesUri}/999999",
            new { name = "Inexistente", kind = "recurring", defaultAmount = 0m });

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task UpdateIncome_BlankName_ReturnsValidationProblemAndKeepsIncome(string? name)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Salário", "recurring", 5000m);

        // Act
        using var response = await client.PutAsJsonAsync($"{IncomesUri}/{created.Id}",
            new { name, kind = "recurring", defaultAmount = 1m });

        // Assert
        await AssertValidationProblemAsync(response, "name");
        await AssertSingleIncomeAsync(client, created);
    }

    [Test]
    public async Task UpdateIncome_NegativeDefaultAmount_ReturnsValidationProblemAndKeepsIncome()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Salário", "recurring", 5000m);

        // Act
        using var response = await client.PutAsJsonAsync($"{IncomesUri}/{created.Id}",
            new { name = "Salário", kind = "recurring", defaultAmount = -0.01m });

        // Assert
        await AssertValidationProblemAsync(response, "defaultAmount");
        await AssertSingleIncomeAsync(client, created);
    }

    [Test]
    public async Task UpdateIncome_UndefinedNumericKind_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Salário", "recurring", 5000m);

        // Act
        using var response = await client.PutAsJsonAsync($"{IncomesUri}/{created.Id}",
            new { name = "Salário", kind = 7, defaultAmount = 1m });

        // Assert
        await AssertValidationProblemAsync(response, "kind");
    }

    [Test]
    public async Task UpdateIncome_DeactivatedIncome_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Efêmera", "recurring", 500m);
        using var deleteResp = await client.DeleteAsync($"{IncomesUri}/{created.Id}");
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        using var response = await client.PutAsJsonAsync($"{IncomesUri}/{created.Id}",
            new { name = "Revivida", kind = "recurring", defaultAmount = 1m });

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // --- Delete (soft) ---

    [Test]
    public async Task DeleteIncome_Existing_ReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "ARemover", "one_off", 100m);

        // Act
        using var response = await client.DeleteAsync($"{IncomesUri}/{created.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIncome_DeactivatedIncome_DisappearsFromList()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var kept = await CreateIncomeAsync(client, "Mantida", "recurring", 1000m);
        var removed = await CreateIncomeAsync(client, "Efêmera", "recurring", 500m);
        using var deleteResp = await client.DeleteAsync($"{IncomesUri}/{removed.Id}");
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act / Assert
        await AssertSingleIncomeAsync(client, kept);
    }

    [Test]
    public async Task DeleteIncome_OnlyIncome_ListReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Única", "recurring", 500m);
        using var deleteResp = await client.DeleteAsync($"{IncomesUri}/{created.Id}");
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        using var response = await client.GetAsync(IncomesUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIncome_NotFound_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.DeleteAsync($"{IncomesUri}/999999");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteIncome_AlreadyDeactivated_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(client, "Duplo", "one_off", 1m);
        using var firstDelete = await client.DeleteAsync($"{IncomesUri}/{created.Id}");
        Assert.That(firstDelete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        using var response = await client.DeleteAsync($"{IncomesUri}/{created.Id}");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // --- Owner isolation ---

    [Test]
    public async Task ListIncomes_OwnerIsolation_DoesNotSeeOtherUsersIncomes()
    {
        // Arrange — user A creates an income; user B must not see it
        using (var clientA = CreateAuthenticatedClient())
        {
            await CreateIncomeAsync(clientA, "SomenteA", "recurring", 3000m);
        }
        using var clientB = CreateAuthenticatedClient();

        // Act
        using var response = await clientB.GetAsync(IncomesUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task UpdateIncome_OwnerIsolation_ReturnsNotFoundAndKeepsOwnersIncome()
    {
        // Arrange — user A creates an income; user B tries to update it
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(clientA, "DoA", "recurring", 1000m);

        // Act
        using var response = await clientB.PutAsJsonAsync($"{IncomesUri}/{created.Id}",
            new { name = "Hackeada", kind = "one_off", defaultAmount = 999m });

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await AssertSingleIncomeAsync(clientA, created);
    }

    [Test]
    public async Task DeleteIncome_OwnerIsolation_ReturnsNotFoundAndKeepsOwnersIncome()
    {
        // Arrange — user A creates an income; user B tries to deactivate it
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var created = await CreateIncomeAsync(clientA, "DoA2", "one_off", 500m);

        // Act
        using var response = await clientB.DeleteAsync($"{IncomesUri}/{created.Id}");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await AssertSingleIncomeAsync(clientA, created);
    }

    // --- Helpers ---

    private static async Task<IncomeDto> CreateIncomeAsync(HttpClient client, string name, string kind, decimal defaultAmount)
    {
        using var response = await client.PostAsJsonAsync(IncomesUri, new { name, kind, defaultAmount });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    private static async Task AssertSingleIncomeAsync(HttpClient client, IncomeDto expected)
    {
        using var response = await client.GetAsync(IncomesUri);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(body, Is.EqualTo(new[] { expected }));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem!.Status, Is.EqualTo((int)expectedStatus));
        }
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, params string[] expectedFields)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem!.Status, Is.EqualTo(400));
            Assert.That(problem.Errors.Keys, Is.EquivalentTo(expectedFields));
            Assert.That(problem.Errors.Values, Has.All.Not.Empty);
        }
    }

    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);

    private sealed record ProblemBody(int Status, string? Title, string? Detail);

    private sealed record ValidationProblemBody(int Status, Dictionary<string, string[]> Errors);
}
