using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/bills/{billId}/history</c>, covering the header/summary/items
/// payload, period filtering, owner scoping, and authentication.
/// </summary>
[TestFixture]
public sealed class BillHistoryEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-bill-history-{suffix}";

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

    private async Task PayAsync(string uid, long entryId)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/entries/bill/{entryId}/pay", uid, new { });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<(HttpStatusCode Status, BillHistoryResponse? Body)> GetHistoryAsync(
        string uid, long billId,
        int? fromYear = null, int? fromMonth = null, int? toYear = null, int? toMonth = null)
    {
        var query = ""
            + (fromYear.HasValue ? $"?fromYear={fromYear}" : "")
            + (fromMonth.HasValue ? $"{(fromYear.HasValue ? "&" : "?")}fromMonth={fromMonth}" : "")
            + (toYear.HasValue ? $"{(fromYear.HasValue || fromMonth.HasValue ? "&" : "?")}toYear={toYear}" : "")
            + (toMonth.HasValue ? "&toMonth=" + toMonth : "");
        using var req = Req(HttpMethod.Get, $"/api/bills/{billId}/history{query}", uid);
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<BillHistoryResponse>() : null;
        return (resp.StatusCode, body);
    }

    // --- Header + summary + items ---

    [Test]
    public async Task Get_ReturnsHeaderSummaryAndItems()
    {
        // Arrange
        var uid = Uid("full");
        var catId = await GetFirstCategoryIdAsync(uid);
        var esposa = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Rodotos", 150m, 0.5m, esposa);
        var jan = await CreateBillEntryAsync(uid, bill, 2026, 1, 150m);
        await PayAsync(uid, jan);
        await CreateBillEntryAsync(uid, bill, 2026, 2, 150m);

        // Act
        var (status, body) = await GetHistoryAsync(uid, bill);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.BillId, Is.EqualTo(bill));
            Assert.That(body.Name, Is.EqualTo("Rodotos"));
            Assert.That(body.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(body.Person, Is.EqualTo("Esposa"));
            Assert.That(body.Items, Has.Length.EqualTo(2));
            Assert.That(body.Items[0].Year, Is.EqualTo(2026));
            Assert.That(body.Items[0].Month, Is.EqualTo(1));
            Assert.That(body.Items[0].Variation, Is.Null);
            Assert.That(body.Items[1].Month, Is.EqualTo(2));
            Assert.That(body.Summary.AvgEffective, Is.EqualTo(150m));
            Assert.That(body.Summary.MinEffective, Is.EqualTo(150m));
            Assert.That(body.Summary.MaxEffective, Is.EqualTo(150m));
            Assert.That(body.Summary.TotalPaidMyShare, Is.EqualTo(75m)); // 150 * 0.5, only jan is paid
        });
    }

    [Test]
    public async Task Get_UnsharedBill_HasNullPerson()
    {
        // Arrange
        var uid = Uid("unshared");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateBillAsync(uid, catId, "Internet", 100m, 1m, null);
        await CreateBillEntryAsync(uid, bill, 2026, 1, 100m);

        // Act
        var (status, body) = await GetHistoryAsync(uid, bill);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Person, Is.Null);
    }

    // --- Period filtering ---

    [Test]
    public async Task Get_PeriodFilter_NarrowsToWindow()
    {
        // Arrange — entries in Jan, May, Sep; filter to Mar-Jun
        var uid = Uid("period-filter");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateBillAsync(uid, catId, "Rodotos", 150m, 1m, null);
        await CreateBillEntryAsync(uid, bill, 2026, 1, 150m);
        await CreateBillEntryAsync(uid, bill, 2026, 5, 150m);
        await CreateBillEntryAsync(uid, bill, 2026, 9, 150m);

        // Act
        var (status, body) = await GetHistoryAsync(uid, bill, fromYear: 2026, fromMonth: 3, toYear: 2026, toMonth: 6);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Items, Has.Length.EqualTo(1));
            Assert.That(body.Items[0].Month, Is.EqualTo(5));
        });
    }

    // --- Owner scoping ---

    [Test]
    public async Task Get_UnknownBillId_ReturnsNotFound()
    {
        // Arrange
        var uid = Uid("unknown-bill");

        // Act
        var (status, _) = await GetHistoryAsync(uid, billId: 999_999_999L);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Get_BillBelongingToAnotherOwner_ReturnsNotFound()
    {
        // Arrange
        var uidA = Uid("owner-a");
        var uidB = Uid("owner-b");
        var catIdA = await GetFirstCategoryIdAsync(uidA);
        var billA = await CreateBillAsync(uidA, catIdA, "Rodotos", 150m, 1m, null);
        await GetFirstCategoryIdAsync(uidB); // provision B

        // Act — B tries to view A's bill history
        var (status, _) = await GetHistoryAsync(uidB, billA);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Auth ---

    [Test]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/bills/1/history");

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

    private sealed record BillHistoryVariationResponse(decimal Abs, decimal? Pct);

    private sealed record BillHistoryItemResponse(
        int Year, int Month, decimal PlannedAmount, decimal? ActualAmount, decimal Effective, decimal MyShare,
        bool Paid, DateTimeOffset? PaidDate, BillHistoryVariationResponse? Variation);

    private sealed record BillHistorySummaryResponse(
        decimal AvgEffective, decimal MinEffective, decimal MaxEffective, decimal TotalPaidMyShare);

    private sealed record BillHistoryResponse(
        long BillId, string Name, string Category, decimal SplitRatio, string? Person,
        BillHistorySummaryResponse Summary, BillHistoryItemResponse[] Items);
}
