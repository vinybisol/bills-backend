using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the authenticated income CRUD endpoints, covering the full
/// request pipeline: JWT validation, owner isolation, and soft delete.
/// </summary>
[TestFixture]
public sealed class IncomeEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-income-{suffix}";

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

    // --- Create ---

    [Test]
    public async Task CreateIncome_WithValidToken_ReturnsCreatedWithDto()
    {
        // Arrange
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", Uid("create-ok"),
            new { name = "Salário", kind = "recurring", defaultAmount = 5000m });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto>();
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Id, Is.GreaterThan(0));
            Assert.That(body.Name, Is.EqualTo("Salário"));
            Assert.That(body.Kind, Is.EqualTo("recurring"));
            Assert.That(body.DefaultAmount, Is.EqualTo(5000m));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateIncome_BlankName_ReturnsBadRequest(string name)
    {
        // Arrange
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", Uid($"create-bad-name-{name.Length}"),
            new { name, kind = "recurring", defaultAmount = 1000m });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase("salary")]
    [TestCase("monthly")]
    [TestCase("")]
    public async Task CreateIncome_InvalidKind_ReturnsBadRequest(string kind)
    {
        // Arrange
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", Uid($"create-bad-kind-{kind.Length}"),
            new { name = "Renda", kind, defaultAmount = 1000m });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateIncome_NegativeDefaultAmount_ReturnsBadRequest()
    {
        // Arrange
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", Uid("create-bad-amount"),
            new { name = "Renda", kind = "recurring", defaultAmount = -1m });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateIncome_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/incomes");
        req.Content = JsonContent.Create(new { name = "Salário", kind = "recurring", defaultAmount = 5000m });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- List ---

    [Test]
    public async Task ListIncomes_NewUser_ReturnsEmptyList()
    {
        // Arrange
        using var req = Req(HttpMethod.Get, "/api/v1/incomes", Uid("list-empty"));

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(body, Is.Empty);
    }

    [Test]
    public async Task ListIncomes_AfterCreating_IncludesNewIncome()
    {
        // Arrange
        var uid = Uid("list-after-create");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name = "Salário", kind = "recurring", defaultAmount = 5000m });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/incomes", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listResp.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(body, Has.Length.EqualTo(1));
        Assert.That(body![0].Name, Is.EqualTo("Salário"));
    }

    [Test]
    public async Task ListIncomes_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.GetAsync("/api/v1/incomes");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Update ---

    [Test]
    public async Task UpdateIncome_Valid_ReturnsOkWithUpdatedDto()
    {
        // Arrange
        var uid = Uid("update-ok");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name = "Salário", kind = "recurring", defaultAmount = 5000m });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResp.Content.ReadFromJsonAsync<IncomeDto>();
        Assert.That(created, Is.Not.Null);

        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/incomes/{created!.Id}", uid,
            new { name = "Freelance", kind = "one_off", defaultAmount = 2500m });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<IncomeDto>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.Name, Is.EqualTo("Freelance"));
            Assert.That(body.Kind, Is.EqualTo("one_off"));
            Assert.That(body.DefaultAmount, Is.EqualTo(2500m));
        });
    }

    [Test]
    public async Task UpdateIncome_NotFound_ReturnsNotFound()
    {
        // Arrange
        using var req = ReqWithBody(HttpMethod.Put, "/api/v1/incomes/999999", Uid("update-notfound"),
            new { name = "Inexistente", kind = "recurring", defaultAmount = 0m });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UpdateIncome_WithoutToken_ReturnsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/incomes/1");
        req.Content = JsonContent.Create(new { name = "x", kind = "recurring", defaultAmount = 0m });
        using var response = await Client.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Delete (soft) ---

    [Test]
    public async Task DeleteIncome_Deactivates_ReturnsNoContent()
    {
        // Arrange
        var uid = Uid("delete-ok");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name = "ARemover", kind = "one_off", defaultAmount = 100m });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<IncomeDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/incomes/{created!.Id}", uid);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIncome_DeactivatedIncome_DisappearsFromList()
    {
        // Arrange
        var uid = Uid("delete-disappears");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name = "Efêmera", kind = "recurring", defaultAmount = 500m });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<IncomeDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/incomes/{created!.Id}", uid);
        using var deleteResp = await Client.SendAsync(deleteReq);
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var listReq = Req(HttpMethod.Get, "/api/v1/incomes", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        var body = await listResp.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(body!.Select(i => i.Name), Does.Not.Contain("Efêmera"));
    }

    [Test]
    public async Task DeleteIncome_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.DeleteAsync("/api/v1/incomes/1");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Owner isolation ---

    [Test]
    public async Task ListIncomes_OwnerIsolation_DoesNotSeeOtherUsersIncomes()
    {
        // Arrange — user A creates an income; user B must not see it
        var uidA = Uid("isolate-list-a");
        var uidB = Uid("isolate-list-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uidA,
            new { name = "SomenteA", kind = "recurring", defaultAmount = 3000m });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/incomes", uidB);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listResp.Content.ReadFromJsonAsync<IncomeDto[]>();
        Assert.That(body!.Select(i => i.Name), Does.Not.Contain("SomenteA"));
        Assert.That(body, Is.Empty);
    }

    [Test]
    public async Task UpdateIncome_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates an income; user B tries to update it
        var uidA = Uid("isolate-update-a");
        var uidB = Uid("isolate-update-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uidA,
            new { name = "DoA", kind = "recurring", defaultAmount = 1000m });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<IncomeDto>();

        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/incomes/{created!.Id}", uidB,
            new { name = "Hackeada", kind = "one_off", defaultAmount = 999m });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteIncome_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates an income; user B tries to deactivate it
        var uidA = Uid("isolate-delete-a");
        var uidB = Uid("isolate-delete-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uidA,
            new { name = "DoA2", kind = "one_off", defaultAmount = 500m });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<IncomeDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/incomes/{created!.Id}", uidB);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
}
