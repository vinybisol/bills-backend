using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for POST /api/bills/{billId}/recalculate.
/// Covers the happy path, paid-entry skipping, DefaultAmount update,
/// owner isolation, auth, and input validation.
/// </summary>
[TestFixture]
public sealed class RecalculateEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-recalc-{suffix}";

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

    private async Task<long> GetFirstCategoryIdAsync(string uid)
    {
        using var req = Req(HttpMethod.Get, "/api/v1/categories", uid);
        using var resp = await Client.SendAsync(req);
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        return dtos![0].Id;
    }

    private async Task<long> CreateRecurringBillAsync(string uid, long categoryId, decimal defaultAmount = 100m)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Energia", categoryId, kind = "recurring", defaultAmount, splitRatio = 1m, personId = (long?)null });
        using var resp = await Client.SendAsync(req);
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!.Id;
    }

    private async Task PostProjectionAsync(string uid, int year)
    {
        using var req = Req(HttpMethod.Post, $"/api/v1/projection/{year}", uid);
        await Client.SendAsync(req);
    }

    private async Task<decimal> GetPlannedAmountAsync(string uid, int year, int month)
    {
        using var req = Req(HttpMethod.Get, $"/api/v1/entries?year={year}&month={month}", uid);
        using var resp = await Client.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>();
        return body!.Bills[0].PlannedAmount;
    }

    private async Task<long> GetBillEntryIdAsync(string uid, int year, int month)
    {
        using var req = Req(HttpMethod.Get, $"/api/v1/entries?year={year}&month={month}", uid);
        using var resp = await Client.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>();
        return body!.Bills[0].Id;
    }

    // --- Happy path ---

    [Test]
    public async Task Recalculate_FromJuly_UpdatesJulyToDecemberUnpaid_LeavesJanuaryToJuneUntouched()
    {
        // Arrange
        var uid = Uid("happy-path");
        var catId = await GetFirstCategoryIdAsync(uid);
        var billId = await CreateRecurringBillAsync(uid, catId, defaultAmount: 100m);
        await PostProjectionAsync(uid, 2026);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/bills/{billId}/recalculate", uid,
            new { fromYear = 2026, fromMonth = 7, newAmount = 175m });
        using var resp = await Client.SendAsync(req);

        // Assert — response
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<RecalculateResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.UpdatedEntries, Is.EqualTo(6)); // Jul-Dec
            Assert.That(body.SkippedPaid, Is.EqualTo(0));
            Assert.That(body.NewDefaultAmount, Is.EqualTo(175m));
        });

        // Assert — months 1-6 are untouched
        for (int m = 1; m <= 6; m++)
            Assert.That(await GetPlannedAmountAsync(uid, 2026, m), Is.EqualTo(100m), $"Month {m} should be untouched");

        // Assert — months 7-12 are updated
        for (int m = 7; m <= 12; m++)
            Assert.That(await GetPlannedAmountAsync(uid, 2026, m), Is.EqualTo(175m), $"Month {m} should be updated");
    }

    [Test]
    public async Task Recalculate_PaidMonthInRange_IsSkipped()
    {
        // Arrange
        var uid = Uid("skip-paid");
        var catId = await GetFirstCategoryIdAsync(uid);
        var billId = await CreateRecurringBillAsync(uid, catId, defaultAmount: 100m);
        await PostProjectionAsync(uid, 2026);

        // Pay month 8
        var entryId = await GetBillEntryIdAsync(uid, 2026, 8);
        using var payReq = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { });
        await Client.SendAsync(payReq);

        // Act — recalculate from July
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/bills/{billId}/recalculate", uid,
            new { fromYear = 2026, fromMonth = 7, newAmount = 200m });
        using var resp = await Client.SendAsync(req);

        // Assert — response counts
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<RecalculateResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.UpdatedEntries, Is.EqualTo(5)); // Jul + Sep-Dec (Aug skipped)
            Assert.That(body.SkippedPaid, Is.EqualTo(1));
        });

        // Assert — paid month 8 is still at 100 (frozen)
        Assert.That(await GetPlannedAmountAsync(uid, 2026, 8), Is.EqualTo(100m));
        // Month 7 is updated
        Assert.That(await GetPlannedAmountAsync(uid, 2026, 7), Is.EqualTo(200m));
    }

    [Test]
    public async Task Recalculate_UpdatesBillDefaultAmount()
    {
        // Arrange
        var uid = Uid("default-amount");
        var catId = await GetFirstCategoryIdAsync(uid);
        var billId = await CreateRecurringBillAsync(uid, catId, defaultAmount: 100m);
        await PostProjectionAsync(uid, 2026);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/bills/{billId}/recalculate", uid,
            new { fromYear = 2026, fromMonth = 1, newAmount = 150m });
        await Client.SendAsync(req);

        // Assert — GET /bills reflects the new DefaultAmount
        using var billReq = Req(HttpMethod.Get, "/api/v1/bills", uid);
        using var billResp = await Client.SendAsync(billReq);
        var bills = await billResp.Content.ReadFromJsonAsync<BillSummaryDto[]>();
        Assert.That(bills![0].DefaultAmount, Is.EqualTo(150m));
    }

    // --- Owner isolation ---

    [Test]
    public async Task Recalculate_OtherOwnerBill_Returns404()
    {
        // Arrange — A creates a bill; B tries to recalculate it
        var uidA = Uid("owner-a");
        var uidB = Uid("owner-b");
        var catId = await GetFirstCategoryIdAsync(uidA);
        var billId = await CreateRecurringBillAsync(uidA, catId);

        _ = await GetFirstCategoryIdAsync(uidB); // provision B

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/bills/{billId}/recalculate", uidB,
            new { fromYear = 2026, fromMonth = 7, newAmount = 175m });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Auth ---

    [Test]
    public async Task Recalculate_WithoutToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bills/1/recalculate");
        req.Content = JsonContent.Create(new { fromYear = 2026, fromMonth = 7, newAmount = 100m });
        using var resp = await Client.SendAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Validation ---

    [Test]
    [TestCase(0)]
    [TestCase(13)]
    public async Task Recalculate_InvalidMonth_Returns400(int month)
    {
        // Arrange
        var uid = Uid($"invalid-month-{month}");
        var catId = await GetFirstCategoryIdAsync(uid);
        var billId = await CreateRecurringBillAsync(uid, catId);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/bills/{billId}/recalculate", uid,
            new { fromYear = 2026, fromMonth = month, newAmount = 100m });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Recalculate_NegativeAmount_Returns400()
    {
        // Arrange
        var uid = Uid("negative-amount");
        var catId = await GetFirstCategoryIdAsync(uid);
        var billId = await CreateRecurringBillAsync(uid, catId);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/bills/{billId}/recalculate", uid,
            new { fromYear = 2026, fromMonth = 7, newAmount = -1m });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Recalculate_BillNotFound_Returns404()
    {
        // Arrange — provision user but use a non-existent bill id
        var uid = Uid("not-found");
        _ = await GetFirstCategoryIdAsync(uid);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills/999999/recalculate", uid,
            new { fromYear = 2026, fromMonth = 7, newAmount = 100m });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Local DTOs ---

    private sealed record RecalculateResponse(long BillId, int UpdatedEntries, int SkippedPaid, decimal NewDefaultAmount);
    private sealed record MonthEntriesResponse(int Year, int Month, EntryDto[] Bills, object[] Incomes);
    private sealed record EntryDto(long Id, decimal PlannedAmount, bool Paid);
    private sealed record BillDto(long Id);
    private sealed record BillSummaryDto(long Id, string Name, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
}
