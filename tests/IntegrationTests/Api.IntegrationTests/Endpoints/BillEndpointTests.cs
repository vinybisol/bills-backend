using System.Net;
using System.Net.Http.Json;
using Api.IntegrationTests.TestSupport;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for the <c>/api/v1/bills</c> CRUD endpoints, covering the full request
/// pipeline: JWT validation, validation problems (split/person rule), category/person references,
/// owner isolation, and soft delete. Each test authenticates as a fresh Firebase uid.
/// </summary>
[TestFixture]
public sealed class BillEndpointTests : IntegrationTestBase
{
    private const string BillsUri = "/api/v1/bills";

    [TestCase("", "GET")]
    [TestCase("", "POST")]
    [TestCase("/100", "PUT")]
    [TestCase("/100", "DELETE")]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized(string uri, string method)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{BillsUri}{uri}");
        if (method is "POST" or "PUT")
            request.Content = JsonContent.Create(BillBody("Aluguel", 1L));

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Create ---

    [Test]
    public async Task CreateBill_WithValidToken_ReturnsCreatedWithDtoAndLocation()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", categoryIds[0], defaultAmount: 1500m));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<BillDto>();
        Assert.That(body, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Id, Is.GreaterThan(0));
            Assert.That(body, Is.EqualTo(new BillDto(body.Id, "Aluguel", categoryIds[0], "recurring", 1500m, 1m, null)));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"{BillsUri}/{body.Id}"));
        }
    }

    [Test]
    public async Task CreateBill_SharedWithPerson_ReturnsCreatedWithSplit()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var personId = await CreatePersonAsync(client);

        // Act
        var created = await CreateBillAsync(client, "  Internet  ", categoryIds[0], kind: "one_off", splitRatio: 0.5m, personId: personId);

        // Assert
        Assert.That(created, Is.EqualTo(new BillDto(created.Id, "Internet", categoryIds[0], "one_off", 100m, 0.5m, personId)));
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task CreateBill_BlankName_ReturnsValidationProblem(string? name)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody(name, 1L));

        // Assert
        await AssertValidationProblemAsync(response, "name");
    }

    [TestCase(-0.01)]
    [TestCase(1.5)]
    public async Task CreateBill_SplitRatioOutOfRange_ReturnsValidationProblem(decimal splitRatio)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", 1L, splitRatio: splitRatio));

        // Assert
        await AssertValidationProblemAsync(response, "splitRatio");
    }

    [Test]
    public async Task CreateBill_SplitLessThan1WithoutPerson_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", 1L, splitRatio: 0.5m));

        // Assert
        await AssertValidationProblemAsync(response, "personId");
    }

    [Test]
    public async Task CreateBill_SplitEquals1WithPerson_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", 1L, personId: 2L));

        // Assert
        await AssertValidationProblemAsync(response, "personId");
    }

    [Test]
    public async Task CreateBill_NegativeDefaultAmount_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", 1L, defaultAmount: -1m));

        // Assert
        await AssertValidationProblemAsync(response, "defaultAmount");
    }

    [Test]
    public async Task CreateBill_UndefinedNumericKind_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri,
            new { name = "Aluguel", categoryId = 1L, kind = 5, defaultAmount = 1m, splitRatio = 1m, personId = (long?)null });

        // Assert
        await AssertValidationProblemAsync(response, "kind");
    }

    [TestCase("monthly")]
    [TestCase("")]
    public async Task CreateBill_UnknownKindString_ReturnsBadRequest(string kind)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", 1L, kind: kind));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(response.Headers.Location, Is.Null);
        }
    }

    [Test]
    public async Task CreateBill_AllFieldsInvalid_ReturnsOneErrorPerField()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri,
            new { name = " ", categoryId = 1L, kind = 9, defaultAmount = -1m, splitRatio = 2m, personId = (long?)null });

        // Assert
        await AssertValidationProblemAsync(response, "name", "kind", "defaultAmount", "splitRatio");
    }

    [Test]
    public async Task CreateBill_Invalid_DoesNotPersistAnything()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        using var createResp = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", categoryIds[0], splitRatio: 0.5m));
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // Act
        using var listResp = await client.GetAsync(BillsUri);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task CreateBill_CategoryNotFound_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri, BillBody("Aluguel", 999999L));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CreateBill_PersonNotFound_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);

        // Act
        using var response = await client.PostAsJsonAsync(BillsUri,
            BillBody("Aluguel", categoryIds[0], splitRatio: 0.5m, personId: 999999L));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CreateBill_OtherOwnersCategory_ReturnsNotFoundProblem()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var categoryIdsA = await GetDefaultCategoryIdsAsync(clientA);

        // Act
        using var response = await clientB.PostAsJsonAsync(BillsUri, BillBody("Aluguel", categoryIdsA[0]));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CreateBill_OtherOwnersPerson_ReturnsNotFoundProblem()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var personIdA = await CreatePersonAsync(clientA);
        var categoryIdsB = await GetDefaultCategoryIdsAsync(clientB);

        // Act
        using var response = await clientB.PostAsJsonAsync(BillsUri,
            BillBody("Aluguel", categoryIdsB[0], splitRatio: 0.5m, personId: personIdA));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // --- List ---

    [Test]
    public async Task ListBills_NewUser_ReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync(BillsUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task ListBills_MultipleBills_ReturnsOrderedByName()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateBillAsync(client, "Internet", categoryIds[0]);
        await CreateBillAsync(client, "Aluguel", categoryIds[0]);
        await CreateBillAsync(client, "Energia", categoryIds[1]);

        // Act
        using var response = await client.GetAsync(BillsUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BillDto[]>();
        Assert.That(body!.Select(b => b.Name), Is.EqualTo(new[] { "Aluguel", "Energia", "Internet" }));
    }

    // --- Update ---

    [Test]
    public async Task UpdateBill_Valid_ReturnsOkWithUpdatedDto()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var personId = await CreatePersonAsync(client);
        var created = await CreateBillAsync(client, "Aluguel", categoryIds[0], defaultAmount: 1500m);

        // Act
        using var response = await client.PutAsJsonAsync($"{BillsUri}/{created.Id}",
            BillBody("  Carro  ", categoryIds[1], kind: "one_off", defaultAmount: 800m, splitRatio: 0.5m, personId: personId));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BillDto>();
        Assert.That(body, Is.EqualTo(new BillDto(created.Id, "Carro", categoryIds[1], "one_off", 800m, 0.5m, personId)));
        await AssertSingleBillAsync(client, body!);
    }

    [Test]
    public async Task UpdateBill_NotFound_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PutAsJsonAsync($"{BillsUri}/999999", BillBody("Inexistente", 1L));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateBill_SplitLessThan1WithoutPerson_ReturnsValidationProblemAndKeepsBill()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var created = await CreateBillAsync(client, "Aluguel", categoryIds[0]);

        // Act
        using var response = await client.PutAsJsonAsync($"{BillsUri}/{created.Id}",
            BillBody("Aluguel", categoryIds[0], splitRatio: 0.3m));

        // Assert
        await AssertValidationProblemAsync(response, "personId");
        await AssertSingleBillAsync(client, created);
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task UpdateBill_BlankName_ReturnsValidationProblemAndKeepsBill(string? name)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var created = await CreateBillAsync(client, "Aluguel", categoryIds[0]);

        // Act
        using var response = await client.PutAsJsonAsync($"{BillsUri}/{created.Id}", BillBody(name, categoryIds[0]));

        // Assert
        await AssertValidationProblemAsync(response, "name");
        await AssertSingleBillAsync(client, created);
    }

    [Test]
    public async Task UpdateBill_CategoryNotFound_ReturnsNotFoundProblemAndKeepsBill()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var created = await CreateBillAsync(client, "Aluguel", categoryIds[0]);

        // Act
        using var response = await client.PutAsJsonAsync($"{BillsUri}/{created.Id}", BillBody("Carro", 999999L));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await AssertSingleBillAsync(client, created);
    }

    [Test]
    public async Task UpdateBill_PersonNotFound_ReturnsNotFoundProblemAndKeepsBill()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var created = await CreateBillAsync(client, "Aluguel", categoryIds[0]);

        // Act
        using var response = await client.PutAsJsonAsync($"{BillsUri}/{created.Id}",
            BillBody("Aluguel", categoryIds[0], splitRatio: 0.5m, personId: 999999L));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await AssertSingleBillAsync(client, created);
    }

    [Test]
    public async Task UpdateBill_DeactivatedBill_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var created = await CreateBillAsync(client, "Efêmera", categoryIds[0]);
        using var deleteResp = await client.DeleteAsync($"{BillsUri}/{created.Id}");
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        using var response = await client.PutAsJsonAsync($"{BillsUri}/{created.Id}", BillBody("Revivida", categoryIds[0]));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // --- Delete (soft) ---

    [Test]
    public async Task DeleteBill_Existing_ReturnsNoContentAndDisappearsFromList()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var kept = await CreateBillAsync(client, "Mantida", categoryIds[0]);
        var removed = await CreateBillAsync(client, "Efêmera", categoryIds[0]);

        // Act
        using var response = await client.DeleteAsync($"{BillsUri}/{removed.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await AssertSingleBillAsync(client, kept);
    }

    [Test]
    public async Task DeleteBill_OnlyBill_ListReturnsNoContent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var created = await CreateBillAsync(client, "Única", categoryIds[0]);
        using var deleteResp = await client.DeleteAsync($"{BillsUri}/{created.Id}");
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        using var response = await client.GetAsync(BillsUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteBill_NotFound_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.DeleteAsync($"{BillsUri}/999999");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteBill_AlreadyDeactivated_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var created = await CreateBillAsync(client, "Duplo", categoryIds[0]);
        using var firstDelete = await client.DeleteAsync($"{BillsUri}/{created.Id}");
        Assert.That(firstDelete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        using var response = await client.DeleteAsync($"{BillsUri}/{created.Id}");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // --- Owner isolation ---

    [Test]
    public async Task ListBills_OwnerIsolation_DoesNotSeeOtherUsersBills()
    {
        // Arrange — user A creates a bill; user B must not see it
        using (var clientA = CreateAuthenticatedClient())
        {
            var categoryIds = await GetDefaultCategoryIdsAsync(clientA);
            await CreateBillAsync(clientA, "SomenteA", categoryIds[0]);
        }
        using var clientB = CreateAuthenticatedClient();

        // Act
        using var response = await clientB.GetAsync(BillsUri);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task UpdateBill_OwnerIsolation_ReturnsNotFoundAndKeepsOwnersBill()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var categoryIdsA = await GetDefaultCategoryIdsAsync(clientA);
        var categoryIdsB = await GetDefaultCategoryIdsAsync(clientB);
        var created = await CreateBillAsync(clientA, "DoA", categoryIdsA[0]);

        // Act
        using var response = await clientB.PutAsJsonAsync($"{BillsUri}/{created.Id}",
            BillBody("Hackeada", categoryIdsB[0], defaultAmount: 999m));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await AssertSingleBillAsync(clientA, created);
    }

    [Test]
    public async Task DeleteBill_OwnerIsolation_ReturnsNotFoundAndKeepsOwnersBill()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var categoryIdsA = await GetDefaultCategoryIdsAsync(clientA);
        var created = await CreateBillAsync(clientA, "DoA2", categoryIdsA[0]);

        // Act
        using var response = await clientB.DeleteAsync($"{BillsUri}/{created.Id}");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await AssertSingleBillAsync(clientA, created);
    }

    // --- Helpers ---

    private static object BillBody(
        string? name, long categoryId, string kind = "recurring", decimal defaultAmount = 100m,
        decimal splitRatio = 1m, long? personId = null) =>
        new { name, categoryId, kind, defaultAmount, splitRatio, personId };

    // Every new user has default categories seeded on first authenticated request.
    private static async Task<long[]> GetDefaultCategoryIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/categories");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await response.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Has.Length.GreaterThanOrEqualTo(2), "Expected seeded default categories.");
        return [.. dtos!.Select(c => c.Id)];
    }

    private static async Task<long> CreatePersonAsync(HttpClient client, string name = "Parceiro")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/persons", new { name });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    private static async Task<BillDto> CreateBillAsync(
        HttpClient client, string name, long categoryId, string kind = "recurring", decimal defaultAmount = 100m,
        decimal splitRatio = 1m, long? personId = null)
    {
        using var response = await client.PostAsJsonAsync(BillsUri,
            BillBody(name, categoryId, kind, defaultAmount, splitRatio, personId));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<BillDto>())!;
    }

    private static async Task AssertSingleBillAsync(HttpClient client, BillDto expected)
    {
        using var response = await client.GetAsync(BillsUri);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BillDto[]>();
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

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

    private sealed record CategoryDto(long Id, string Name);

    private sealed record PersonDto(long Id, string Name);

    private sealed record ProblemBody(int Status, string? Title, string? Detail);

    private sealed record ValidationProblemBody(int Status, Dictionary<string, string[]> Errors);
}
