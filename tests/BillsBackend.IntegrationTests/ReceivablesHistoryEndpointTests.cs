using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/receivables/history</c>, covering item/total scoping to
/// owner and person, period filtering, status filtering, unknown/other-owner person handling,
/// and authentication.
/// </summary>
[TestFixture]
public sealed class ReceivablesHistoryEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-receivables-history-{suffix}";

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

    // --- Setup helpers ---

    private async Task<long> GetFirstCategoryIdAsync(string uid)
    {
        using var req = Req(HttpMethod.Get, "/categories", uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos![0].Id;
    }

    private async Task<long> CreatePersonAsync(string uid, string name)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/persons", uid, new { name });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    private async Task<long> CreateBillAsync(
        string uid, long categoryId, string name, decimal amount, decimal splitRatio, long? personId)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/bills", uid,
            new { name, categoryId, kind = "one_off", defaultAmount = amount, splitRatio, personId });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!.Id;
    }

    private async Task<long> CreateBillEntryAsync(string uid, long billId, int year, int month, decimal plannedAmount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/entries/bill", uid,
            new { billId, year, month, plannedAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillEntryResponse>())!.Id;
    }

    private async Task MarkAsync(string uid, long entryId)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/receivables/{entryId}/mark", uid, new { });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<(HttpStatusCode Status, ReceivablesHistoryResponse? Body)> GetHistoryAsync(
        string uid, long personId,
        int? fromYear = null, int? fromMonth = null, int? toYear = null, int? toMonth = null, string? status = null)
    {
        var query = $"?personId={personId}"
            + (fromYear.HasValue ? $"&fromYear={fromYear}" : "")
            + (fromMonth.HasValue ? $"&fromMonth={fromMonth}" : "")
            + (toYear.HasValue ? $"&toYear={toYear}" : "")
            + (toMonth.HasValue ? $"&toMonth={toMonth}" : "")
            + (status is not null ? $"&status={status}" : "");
        using var req = Req(HttpMethod.Get, $"/api/receivables/history{query}", uid);
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ReceivablesHistoryResponse>() : null;
        return (resp.StatusCode, body);
    }

    // --- Items + totals scoping ---

    [Test]
    public async Task Get_ReturnsItemsAndTotalsScopedToOwnerAndPerson()
    {
        // Arrange
        var uid = Uid("scoped");
        var catId = await GetFirstCategoryIdAsync(uid);
        var esposa = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, esposa);
        var received = await CreateBillEntryAsync(uid, bill, 2026, 1, 1000m);
        await MarkAsync(uid, received);
        await CreateBillEntryAsync(uid, bill, 2026, 2, 1000m);

        // Act
        var (status, body) = await GetHistoryAsync(uid, esposa);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.PersonId, Is.EqualTo(esposa));
            Assert.That(body.Items, Has.Length.EqualTo(2));
            Assert.That(body.Totals.TotalDevido, Is.EqualTo(1000m)); // 500 + 500
            Assert.That(body.Totals.TotalRecebido, Is.EqualTo(500m));
            Assert.That(body.Totals.TotalPendente, Is.EqualTo(500m));
            Assert.That(body.Totals.TotalRecebido + body.Totals.TotalPendente, Is.EqualTo(body.Totals.TotalDevido));
        });
    }

    [Test]
    public async Task Get_ExcludesOtherOwnersEntries()
    {
        // Arrange
        var uidA = Uid("owner-a");
        var uidB = Uid("owner-b");
        var catIdA = await GetFirstCategoryIdAsync(uidA);
        var personA = await CreatePersonAsync(uidA, "Esposa");
        var billA = await CreateBillAsync(uidA, catIdA, "Aluguel", 1000m, 0.5m, personA);
        await CreateBillEntryAsync(uidA, billA, 2026, 3, 1000m);

        await GetFirstCategoryIdAsync(uidB); // provision B

        // Act
        var (status, body) = await GetHistoryAsync(uidA, personA);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Items, Has.Length.EqualTo(1));
    }

    // --- Period filtering ---

    [Test]
    public async Task Get_PeriodFilter_NarrowsToWindow()
    {
        // Arrange — entries in Jan, May, Sep; filter to Mar-Jun
        var uid = Uid("period-filter");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        await CreateBillEntryAsync(uid, bill, 2026, 1, 1000m);
        await CreateBillEntryAsync(uid, bill, 2026, 5, 1000m);
        await CreateBillEntryAsync(uid, bill, 2026, 9, 1000m);

        // Act
        var (status, body) = await GetHistoryAsync(uid, person, fromYear: 2026, fromMonth: 3, toYear: 2026, toMonth: 6);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Items, Has.Length.EqualTo(1));
            Assert.That(body.Items[0].Month, Is.EqualTo(5));
        });
    }

    // --- Status filtering ---

    [Test]
    public async Task Get_StatusFilter_Received_NarrowsToReceivedOnly()
    {
        // Arrange
        var uid = Uid("status-received");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var receivedEntry = await CreateBillEntryAsync(uid, bill, 2026, 1, 1000m);
        await MarkAsync(uid, receivedEntry);
        await CreateBillEntryAsync(uid, bill, 2026, 2, 1000m);

        // Act
        var (status, body) = await GetHistoryAsync(uid, person, status: "received");

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Items, Has.Length.EqualTo(1));
            Assert.That(body.Items[0].Received, Is.True);
        });
    }

    [Test]
    public async Task Get_StatusFilter_Pending_NarrowsToPendingOnly()
    {
        // Arrange
        var uid = Uid("status-pending");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var receivedEntry = await CreateBillEntryAsync(uid, bill, 2026, 1, 1000m);
        await MarkAsync(uid, receivedEntry);
        await CreateBillEntryAsync(uid, bill, 2026, 2, 1000m);

        // Act
        var (status, body) = await GetHistoryAsync(uid, person, status: "pending");

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Items, Has.Length.EqualTo(1));
            Assert.That(body.Items[0].Received, Is.False);
        });
    }

    // --- Unknown / other-owner person ---

    [Test]
    public async Task Get_UnknownPersonId_ReturnsNotFound()
    {
        // Arrange
        var uid = Uid("unknown-person");

        // Act
        var (status, _) = await GetHistoryAsync(uid, personId: 999_999_999L);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Get_PersonBelongingToAnotherOwner_ReturnsNotFound()
    {
        // Arrange
        var uidA = Uid("person-owner-a");
        var uidB = Uid("person-owner-b");
        var personA = await CreatePersonAsync(uidA, "Esposa");
        await GetFirstCategoryIdAsync(uidB); // provision B

        // Act — B tries to view A's person history
        var (status, _) = await GetHistoryAsync(uidB, personA);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Auth ---

    [Test]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/receivables/history?personId=1");

        // Act
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Local helpers / DTOs for JSON deserialization ---

    private sealed record BillDto(long Id, string Name);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
    private sealed record BillEntryResponse(long Id, long BillId, int RefYear, int RefMonth);

    private sealed record ReceivablesHistoryItemResponse(
        long EntryId, string Bill, int Year, int Month, decimal Receivable, bool Received, DateTimeOffset? ReceivedDate);

    private sealed record ReceivablesHistoryTotalsResponse(decimal TotalDevido, decimal TotalRecebido, decimal TotalPendente);

    private sealed record ReceivablesHistoryResponse(
        long PersonId, string Name, ReceivablesHistoryTotalsResponse Totals, ReceivablesHistoryItemResponse[] Items);
}
