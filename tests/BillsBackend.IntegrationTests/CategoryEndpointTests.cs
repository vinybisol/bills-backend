using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillsBackend.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the authenticated category CRUD endpoints, covering the full
/// request pipeline: JWT validation, owner isolation, soft delete, and seeding.
/// </summary>
[TestFixture]
public sealed class CategoryEndpointTests : IntegrationTestBase
{
    // Each test uses a fresh firebase uid to document the scenario and keep tests independent.
    private static string Uid(string suffix) => $"firebase-cat-{suffix}";

    // Each uid carries its own e-mail: app_user enforces a unique index on email, so the
    // owner-isolation tests (which provision two distinct users) must not share an address.
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

    // --- Seeding ---

    [Test]
    public async Task ListCategories_NewUser_ReturnsSevenDefaultCategories()
    {
        // Arrange
        using var req = Req(HttpMethod.Get, "/api/v1/categories", Uid("list-seed"));

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(body, Has.Length.EqualTo(7));
    }

    // --- Create ---

    [Test]
    public async Task CreateCategory_WithValidToken_ReturnsCreatedWithDto()
    {
        // Arrange
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/categories", Uid("create-ok"), new { name = "Vestuário" });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Id, Is.GreaterThan(0));
        Assert.That(body.Name, Is.EqualTo("Vestuário"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateCategory_BlankName_ReturnsBadRequest(string name)
    {
        // Arrange
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/categories", Uid($"create-bad-{name.Length}"), new { name });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateCategory_DuplicateName_ReturnsConflict()
    {
        // Arrange — create a category then try to create another with the same name
        var uid = Uid("create-dup");
        using var req1 = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "Duplicada" });
        using var first = await Client.SendAsync(req1);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var req2 = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "Duplicada" });

        // Act
        using var response = await Client.SendAsync(req2);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task CreateCategory_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/categories");
        req.Content = JsonContent.Create(new { name = "Teste" });

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- List ---

    [Test]
    public async Task ListCategories_AfterCreating_IncludesNewCategory()
    {
        // Arrange — create a custom category for a fresh user
        var uid = Uid("list-after-create");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "Beleza" });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/categories", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert — 7 defaults + 1 custom
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listResp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(body, Has.Length.EqualTo(8));
        Assert.That(body!.Select(c => c.Name), Does.Contain("Beleza"));
    }

    [Test]
    public async Task ListCategories_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.GetAsync("/api/v1/categories");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Update ---

    [Test]
    public async Task UpdateCategory_Rename_ReturnsOkWithUpdatedName()
    {
        // Arrange — create a category and capture its id
        var uid = Uid("update-ok");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "Original" });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResp.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.That(created, Is.Not.Null);

        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/categories/{created!.Id}", uid, new { name = "Renomeada" });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.That(body!.Name, Is.EqualTo("Renomeada"));
    }

    [Test]
    public async Task UpdateCategory_DuplicateName_ReturnsConflict()
    {
        // Arrange — create two categories then try to rename one to the other's name
        var uid = Uid("update-dup");
        using var r1 = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "Alpha" });
        using var r2 = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "Beta" });
        using var resp1 = await Client.SendAsync(r1);
        var cat1 = await resp1.Content.ReadFromJsonAsync<CategoryDto>();
        await Client.SendAsync(r2);

        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/categories/{cat1!.Id}", uid, new { name = "Beta" });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task UpdateCategory_WithoutToken_ReturnsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/categories/1");
        req.Content = JsonContent.Create(new { name = "x" });
        using var response = await Client.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Delete (soft) ---

    [Test]
    public async Task DeleteCategory_Deactivates_ReturnsNoContent()
    {
        // Arrange
        var uid = Uid("delete-ok");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "ARemover" });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<CategoryDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/categories/{created!.Id}", uid);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteCategory_DeactivatedCategory_DisappearsFromList()
    {
        // Arrange — create a category, delete it, then list to confirm it's gone
        var uid = Uid("delete-disappears");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uid, new { name = "Efêmera" });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<CategoryDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/categories/{created!.Id}", uid);
        using var deleteResp = await Client.SendAsync(deleteReq);
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var listReq = Req(HttpMethod.Get, "/api/v1/categories", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert — only the 7 defaults remain (the custom one was deactivated)
        var body = await listResp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(body!.Select(c => c.Name), Does.Not.Contain("Efêmera"));
    }

    [Test]
    public async Task DeleteCategory_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.DeleteAsync("/api/v1/categories/1");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Owner isolation ---

    [Test]
    public async Task ListCategories_OwnerIsolation_DoesNotSeeOtherUsersCategories()
    {
        // Arrange — user A creates a category, user B should not see it
        var uidA = Uid("isolate-list-a");
        var uidB = Uid("isolate-list-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uidA, new { name = "SomenteA" });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/categories", uidB);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert — user B sees only their own 7 defaults, not user A's "SomenteA"
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listResp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(body!.Select(c => c.Name), Does.Not.Contain("SomenteA"));
        Assert.That(body, Has.Length.EqualTo(7));
    }

    [Test]
    public async Task UpdateCategory_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates a category, user B tries to rename it
        var uidA = Uid("isolate-update-a");
        var uidB = Uid("isolate-update-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uidA, new { name = "DoA" });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<CategoryDto>();

        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/categories/{created!.Id}", uidB, new { name = "Hackeada" });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteCategory_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates a category, user B tries to deactivate it
        var uidA = Uid("isolate-delete-a");
        var uidB = Uid("isolate-delete-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/categories", uidA, new { name = "DoA2" });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<CategoryDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/categories/{created!.Id}", uidB);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed record CategoryDto(long Id, string Name);
}
