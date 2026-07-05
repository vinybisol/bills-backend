using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the authenticated bill CRUD endpoints, covering the full
/// request pipeline: JWT validation, owner isolation, split/person validation, and soft delete.
/// </summary>
[TestFixture]
public sealed class BillEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-bill-{suffix}";

    private HttpRequestMessage Req(HttpMethod method, string url, string uid) =>
        new(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateValidToken(uid, email: $"{uid}@example.com")) }
        };

    private HttpRequestMessage ReqWithBody<T>(HttpMethod method, string url, string uid, T body)
    {
        var req = Req(method, url, uid);
        req.Content = JsonContent.Create(body);
        return req;
    }

    // Every new user has 7 default categories seeded on first authenticated request.
    // Fetching them avoids name conflicts with those seeded defaults.
    private async Task<long[]> GetDefaultCategoryIdsAsync(string uid)
    {
        using var req = Req(HttpMethod.Get, "/api/v1/categories", uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos!.Select(c => c.Id).ToArray();
    }

    // Creates a person for the given uid and returns their id.
    private async Task<long> CreatePersonAsync(string uid, string name = "Parceiro")
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var dto = await resp.Content.ReadFromJsonAsync<PersonDto>();
        return dto!.Id;
    }

    // --- Create ---

    [Test]
    public async Task CreateBill_WithValidToken_ReturnsCreatedWithDto()
    {
        // Arrange
        var uid = Uid("create-ok");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = categoryIds[0], kind = "recurring", defaultAmount = 1500m, splitRatio = 1m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<BillDto>();
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Id, Is.GreaterThan(0));
            Assert.That(body.Name, Is.EqualTo("Aluguel"));
            Assert.That(body.CategoryId, Is.EqualTo(categoryIds[0]));
            Assert.That(body.Kind, Is.EqualTo("recurring"));
            Assert.That(body.DefaultAmount, Is.EqualTo(1500m));
            Assert.That(body.SplitRatio, Is.EqualTo(1m));
            Assert.That(body.PersonId, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateBill_BlankName_ReturnsBadRequest(string name)
    {
        // Arrange — validation fires before DB access; any categoryId is fine here
        var uid = Uid($"create-bad-name-{name.Length}");
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name, categoryId = 1L, kind = "recurring", defaultAmount = 500m, splitRatio = 1m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateBill_InvalidSplitRatio_ReturnsBadRequest()
    {
        // Arrange — splitRatio > 1 is invalid; validation fires before any DB access
        var uid = Uid("create-bad-ratio");
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = 1L, kind = "recurring", defaultAmount = 500m, splitRatio = 1.5m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateBill_SplitLessThan1_WithoutPerson_ReturnsBadRequest()
    {
        // Arrange — personId is required when splitRatio < 1; validation fires before any DB access
        var uid = Uid("create-split-no-person");
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = 1L, kind = "recurring", defaultAmount = 500m, splitRatio = 0.5m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateBill_SplitEquals1_WithPerson_ReturnsBadRequest()
    {
        // Arrange — personId must be null when splitRatio = 1; validation fires before any DB access
        var uid = Uid("create-split1-with-person");
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = 1L, kind = "recurring", defaultAmount = 500m, splitRatio = 1m, personId = (long?)2L });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateBill_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bills");
        req.Content = JsonContent.Create(new { name = "Aluguel", categoryId = 1L, kind = "recurring", defaultAmount = 500m, splitRatio = 1m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task CreateBill_CategoryNotFound_ReturnsNotFound()
    {
        // Arrange — category 999999 does not belong to this owner; checked after auth
        var uid = Uid("create-no-cat");
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = 999999L, kind = "recurring", defaultAmount = 500m, splitRatio = 1m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateBill_PersonNotFound_ReturnsNotFound()
    {
        // Arrange — category exists (default), person 999999 does not belong to this owner
        var uid = Uid("create-no-person");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = categoryIds[0], kind = "recurring", defaultAmount = 500m, splitRatio = 0.5m, personId = (long?)999999L });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- List ---

    [Test]
    public async Task ListBills_NewUser_ReturnsEmptyList()
    {
        // Arrange
        using var req = Req(HttpMethod.Get, "/api/v1/bills", Uid("list-empty"));

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BillDto[]>();
        Assert.That(body, Is.Empty);
    }

    [Test]
    public async Task ListBills_AfterCreating_IncludesNewBill()
    {
        // Arrange
        var uid = Uid("list-after-create");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = categoryIds[0], kind = "recurring", defaultAmount = 1500m, splitRatio = 1m, personId = (long?)null });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/bills", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listResp.Content.ReadFromJsonAsync<BillDto[]>();
        Assert.That(body, Has.Length.EqualTo(1));
        Assert.That(body![0].Name, Is.EqualTo("Aluguel"));
    }

    [Test]
    public async Task ListBills_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.GetAsync("/api/v1/bills");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Update ---

    [Test]
    public async Task UpdateBill_Valid_ReturnsOkWithUpdatedDto()
    {
        // Arrange
        var uid = Uid("update-ok");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        var personId = await CreatePersonAsync(uid);

        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId = categoryIds[0], kind = "recurring", defaultAmount = 1500m, splitRatio = 1m, personId = (long?)null });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResp.Content.ReadFromJsonAsync<BillDto>();
        Assert.That(created, Is.Not.Null);

        // Update to a different category (index 1) and add a split
        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/bills/{created!.Id}", uid,
            new { name = "Carro", categoryId = categoryIds[1], kind = "one_off", defaultAmount = 800m, splitRatio = 0.5m, personId = (long?)personId });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BillDto>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.Name, Is.EqualTo("Carro"));
            Assert.That(body.CategoryId, Is.EqualTo(categoryIds[1]));
            Assert.That(body.Kind, Is.EqualTo("one_off"));
            Assert.That(body.DefaultAmount, Is.EqualTo(800m));
            Assert.That(body.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(body.PersonId, Is.EqualTo(personId));
        });
    }

    [Test]
    public async Task UpdateBill_NotFound_ReturnsNotFound()
    {
        // Arrange — bill 999999 does not exist for this owner
        using var req = ReqWithBody(HttpMethod.Put, "/api/v1/bills/999999", Uid("update-notfound"),
            new { name = "Inexistente", categoryId = 1L, kind = "recurring", defaultAmount = 0m, splitRatio = 1m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UpdateBill_WithoutToken_ReturnsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/bills/1");
        req.Content = JsonContent.Create(new { name = "x", categoryId = 1L, kind = "recurring", defaultAmount = 0m, splitRatio = 1m, personId = (long?)null });
        using var response = await Client.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Delete (soft) ---

    [Test]
    public async Task DeleteBill_Deactivates_ReturnsNoContent()
    {
        // Arrange
        var uid = Uid("delete-ok");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "ARemover", categoryId = categoryIds[0], kind = "one_off", defaultAmount = 100m, splitRatio = 1m, personId = (long?)null });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<BillDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/bills/{created!.Id}", uid);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteBill_DeactivatedBill_DisappearsFromList()
    {
        // Arrange
        var uid = Uid("delete-disappears");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Efemera", categoryId = categoryIds[0], kind = "recurring", defaultAmount = 500m, splitRatio = 1m, personId = (long?)null });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<BillDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/bills/{created!.Id}", uid);
        using var deleteResp = await Client.SendAsync(deleteReq);
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var listReq = Req(HttpMethod.Get, "/api/v1/bills", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        var body = await listResp.Content.ReadFromJsonAsync<BillDto[]>();
        Assert.That(body!.Select(b => b.Name), Does.Not.Contain("Efemera"));
    }

    [Test]
    public async Task DeleteBill_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.DeleteAsync("/api/v1/bills/1");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Owner isolation ---

    [Test]
    public async Task ListBills_OwnerIsolation_DoesNotSeeOtherUsersBills()
    {
        // Arrange — user A creates a bill; user B must not see it
        var uidA = Uid("isolate-list-a");
        var uidB = Uid("isolate-list-b");
        var categoryIds = await GetDefaultCategoryIdsAsync(uidA);
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uidA,
            new { name = "SomenteA", categoryId = categoryIds[0], kind = "recurring", defaultAmount = 500m, splitRatio = 1m, personId = (long?)null });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/bills", uidB);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listResp.Content.ReadFromJsonAsync<BillDto[]>();
        Assert.That(body!.Select(b => b.Name), Does.Not.Contain("SomenteA"));
        Assert.That(body, Is.Empty);
    }

    [Test]
    public async Task UpdateBill_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates a bill; user B tries to update it.
        // HasQueryFilter scopes the bill lookup to the current owner, so B gets 404.
        var uidA = Uid("isolate-update-a");
        var uidB = Uid("isolate-update-b");
        var categoryIdsA = await GetDefaultCategoryIdsAsync(uidA);
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uidA,
            new { name = "DoA", categoryId = categoryIdsA[0], kind = "recurring", defaultAmount = 500m, splitRatio = 1m, personId = (long?)null });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<BillDto>();

        // Provision user B (triggering their own default categories) and attempt update on A's bill
        var categoryIdsB = await GetDefaultCategoryIdsAsync(uidB);
        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/bills/{created!.Id}", uidB,
            new { name = "Hackeada", categoryId = categoryIdsB[0], kind = "recurring", defaultAmount = 999m, splitRatio = 1m, personId = (long?)null });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteBill_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates a bill; user B tries to deactivate it
        var uidA = Uid("isolate-delete-a");
        var uidB = Uid("isolate-delete-b");
        var categoryIds = await GetDefaultCategoryIdsAsync(uidA);
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uidA,
            new { name = "DoA2", categoryId = categoryIds[0], kind = "one_off", defaultAmount = 500m, splitRatio = 1m, personId = (long?)null });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<BillDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/bills/{created!.Id}", uidB);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
}
