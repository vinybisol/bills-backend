using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the authenticated person CRUD endpoints, covering the full
/// request pipeline: JWT validation, owner isolation, and soft delete.
/// </summary>
[TestFixture]
public sealed class PersonEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-person-{suffix}";

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

    // --- List ---

    [Test]
    public async Task ListPersons_NewUser_ReturnsEmptyList()
    {
        // Arrange
        using var req = Req(HttpMethod.Get, "/api/v1/persons", Uid("list-empty"));

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task ListPersons_AfterCreating_IncludesNewPerson()
    {
        // Arrange
        var uid = Uid("list-after-create");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name = "João" });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/persons", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listResp.Content.ReadFromJsonAsync<PersonDto[]>();
        Assert.That(body, Has.Length.EqualTo(1));
        Assert.That(body![0].Name, Is.EqualTo("João"));
    }

    [Test]
    public async Task ListPersons_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.GetAsync("/api/v1/persons");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Update ---

    [Test]
    public async Task UpdatePerson_Rename_ReturnsOkWithUpdatedName()
    {
        // Arrange
        var uid = Uid("update-ok");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name = "Original" });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResp.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(created, Is.Not.Null);

        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/persons/{created!.Id}", uid, new { name = "Renomeada" });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<PersonDto>();
        Assert.That(body!.Name, Is.EqualTo("Renomeada"));
    }

    [Test]
    public async Task UpdatePerson_WithoutToken_ReturnsUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/persons/1");
        req.Content = JsonContent.Create(new { name = "x" });
        using var response = await Client.SendAsync(req);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Delete (soft) ---

    [Test]
    public async Task DeletePerson_Deactivates_ReturnsNoContent()
    {
        // Arrange
        var uid = Uid("delete-ok");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name = "ARemover" });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<PersonDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/persons/{created!.Id}", uid);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeletePerson_DeactivatedPerson_DisappearsFromList()
    {
        // Arrange
        var uid = Uid("delete-disappears");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name = "Efêmera" });
        using var createReqII = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name = "Second" });
        using var createResp = await Client.SendAsync(createReq);
        using var _ = await Client.SendAsync(createReqII);
        var created = await createResp.Content.ReadFromJsonAsync<PersonDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/persons/{created!.Id}", uid);
        using var deleteResp = await Client.SendAsync(deleteReq);
        Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var listReq = Req(HttpMethod.Get, "/api/v1/persons", uid);

        // Act
        using var listResp = await Client.SendAsync(listReq);

        // Assert
        var body = await listResp.Content.ReadFromJsonAsync<PersonDto[]>();
        Assert.That(body!.Select(p => p.Name), Does.Not.Contain("Efêmera"));
    }

    [Test]
    public async Task DeletePerson_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await Client.DeleteAsync("/api/v1/persons/1");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Owner isolation ---

    [Test]
    public async Task ListPersons_OwnerIsolation_DoesNotSeeOtherUsersPersons()
    {
        // Arrange — user A creates a person; user B must not see it
        var uidA = Uid("isolate-list-a");
        var uidB = Uid("isolate-list-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uidA, new { name = "SomenteA" });
        using var createResp = await Client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var listReq = Req(HttpMethod.Get, "/api/v1/persons", uidB);
        using var listReqA = Req(HttpMethod.Get, "/api/v1/persons", uidA);

        // Act
        using var listResp = await Client.SendAsync(listReq);
        using var listRespA = await Client.SendAsync(listReqA);

        // Assert
        Assert.That(listResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.That(listRespA.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await listRespA.Content.ReadFromJsonAsync<PersonDto[]>();
        Assert.That(body!.Select(p => p.Name), Does.Contain("SomenteA"));
    }

    [Test]
    public async Task UpdatePerson_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates a person; user B tries to rename it
        var uidA = Uid("isolate-update-a");
        var uidB = Uid("isolate-update-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uidA, new { name = "DoA" });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<PersonDto>();

        using var updateReq = ReqWithBody(HttpMethod.Put, $"/api/v1/persons/{created!.Id}", uidB, new { name = "Hackeada" });

        // Act
        using var response = await Client.SendAsync(updateReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeletePerson_OwnerIsolation_ReturnsNotFound()
    {
        // Arrange — user A creates a person; user B tries to deactivate it
        var uidA = Uid("isolate-delete-a");
        var uidB = Uid("isolate-delete-b");
        using var createReq = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uidA, new { name = "DoA2" });
        using var createResp = await Client.SendAsync(createReq);
        var created = await createResp.Content.ReadFromJsonAsync<PersonDto>();

        using var deleteReq = Req(HttpMethod.Delete, $"/api/v1/persons/{created!.Id}", uidB);

        // Act
        using var response = await Client.SendAsync(deleteReq);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed record PersonDto(long Id, string Name);
}
