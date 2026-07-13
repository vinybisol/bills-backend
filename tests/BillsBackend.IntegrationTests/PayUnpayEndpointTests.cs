using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for pay/unpay/patch bill entries and receive/unreceive/patch income entries.
/// Covers immutability (frozen entry blocks edit), unfreeze, amount update, owner isolation, and auth.
/// </summary>
[TestFixture]
public sealed class PayUnpayEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-payunpay-{suffix}";

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
        using var req = Req(HttpMethod.Get, "/api/v1/categories", uid);
        using var resp = await Client.SendAsync(req);
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        return dtos![0].Id;
    }

    private async Task<long> CreateRecurringBillAsync(string uid, long categoryId)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name = "Aluguel", categoryId, kind = "recurring", defaultAmount = 1000m, splitRatio = 1m, personId = (long?)null });
        using var resp = await Client.SendAsync(req);
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!.Id;
    }

    private async Task<long> CreateRecurringIncomeAsync(string uid)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name = "Salario", kind = "recurring", defaultAmount = 5000m });
        using var resp = await Client.SendAsync(req);
        return (await resp.Content.ReadFromJsonAsync<IncomeDto>())!.Id;
    }

    private async Task PostProjectionAsync(string uid, int year)
    {
        using var req = Req(HttpMethod.Post, $"/api/v1/projection/{year}", uid);
        await Client.SendAsync(req);
    }

    private async Task<long> GetBillEntryIdAsync(string uid, int year, int month)
    {
        using var req = Req(HttpMethod.Get, $"/api/v1/entries?year={year}&month={month}", uid);
        using var resp = await Client.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>();
        return body!.Bills[0].Id;
    }

    private async Task<long> GetIncomeEntryIdAsync(string uid, int year, int month)
    {
        using var req = Req(HttpMethod.Get, $"/api/v1/entries?year={year}&month={month}", uid);
        using var resp = await Client.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>();
        return body!.Incomes[0].Id;
    }

    // --- PATCH /api/entries/bill/{id} ---

    [Test]
    public async Task PatchBillEntry_UnpaidEntry_UpdatesAmounts()
    {
        // Arrange
        var uid = Uid("patch-bill-ok");
        var catId = await GetFirstCategoryIdAsync(uid);
        await CreateRecurringBillAsync(uid, catId);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetBillEntryIdAsync(uid, 2025, 1);

        // Act
        using var req = ReqWithBody(new HttpMethod("PATCH"), $"/api/v1/entries/bill/{entryId}", uid,
            new { plannedAmount = 1100m, actualAmount = 1090m });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.PlannedAmount, Is.EqualTo(1100m));
            Assert.That(body.ActualAmount, Is.EqualTo(1090m));
            Assert.That(body.Paid, Is.False);
        });
    }

    [Test]
    public async Task PatchBillEntry_PaidEntry_Returns409()
    {
        // Arrange
        var uid = Uid("patch-bill-frozen");
        var catId = await GetFirstCategoryIdAsync(uid);
        await CreateRecurringBillAsync(uid, catId);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetBillEntryIdAsync(uid, 2025, 2);

        // Pay it first
        using var payReq = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { });
        using var payResp = await Client.SendAsync(payReq);
        Assert.That(payResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act — try to PATCH a paid (frozen) entry
        using var patchReq = ReqWithBody(new HttpMethod("PATCH"), $"/api/v1/entries/bill/{entryId}", uid,
            new { plannedAmount = 1200m });
        using var patchResp = await Client.SendAsync(patchReq);

        // Assert
        Assert.That(patchResp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // --- POST /api/entries/bill/{id}/pay ---

    [Test]
    public async Task PayBillEntry_SetsActualAmountToPlannedWhenOmitted()
    {
        // Arrange
        var uid = Uid("pay-bill-default-actual");
        var catId = await GetFirstCategoryIdAsync(uid);
        await CreateRecurringBillAsync(uid, catId);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetBillEntryIdAsync(uid, 2025, 3);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.Paid, Is.True);
            Assert.That(body.ActualAmount, Is.EqualTo(1000m)); // same as planned
        });
    }

    [Test]
    public async Task PayBillEntry_WithActualAmount_RecordsIt()
    {
        // Arrange
        var uid = Uid("pay-bill-with-actual");
        var catId = await GetFirstCategoryIdAsync(uid);
        await CreateRecurringBillAsync(uid, catId);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetBillEntryIdAsync(uid, 2025, 4);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid,
            new { actualAmount = 980m });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That(body!.ActualAmount, Is.EqualTo(980m));
    }

    // --- POST /api/entries/bill/{id}/unpay ---

    [Test]
    public async Task UnpayBillEntry_FreezeThenUnfreeze_AllowsEdit()
    {
        // Arrange
        var uid = Uid("unpay-bill");
        var catId = await GetFirstCategoryIdAsync(uid);
        await CreateRecurringBillAsync(uid, catId);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetBillEntryIdAsync(uid, 2025, 5);

        // Pay → freeze
        using var payReq = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { });
        await Client.SendAsync(payReq);

        // Unpay → unfreeze
        using var unpayReq = Req(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/unpay", uid);
        using var unpayResp = await Client.SendAsync(unpayReq);
        Assert.That(unpayResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var unpayBody = await unpayResp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That(unpayBody!.Paid, Is.False);

        // PATCH should now succeed
        using var patchReq = ReqWithBody(new HttpMethod("PATCH"), $"/api/v1/entries/bill/{entryId}", uid,
            new { plannedAmount = 1050m });
        using var patchResp = await Client.SendAsync(patchReq);
        Assert.That(patchResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // --- PATCH /api/entries/income/{id} ---

    [Test]
    public async Task PatchIncomeEntry_UnreceivedEntry_UpdatesAmounts()
    {
        // Arrange
        var uid = Uid("patch-income-ok");
        await CreateRecurringIncomeAsync(uid);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetIncomeEntryIdAsync(uid, 2025, 1);

        // Act
        using var req = ReqWithBody(new HttpMethod("PATCH"), $"/api/v1/entries/income/{entryId}", uid,
            new { plannedAmount = 5500m, actualAmount = 5300m });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.PlannedAmount, Is.EqualTo(5500m));
            Assert.That(body.ActualAmount, Is.EqualTo(5300m));
        });
    }

    [Test]
    public async Task PatchIncomeEntry_ReceivedEntry_Returns409()
    {
        // Arrange
        var uid = Uid("patch-income-frozen");
        await CreateRecurringIncomeAsync(uid);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetIncomeEntryIdAsync(uid, 2025, 2);

        // Receive first
        using var recvReq = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/income/{entryId}/receive", uid, new { });
        await Client.SendAsync(recvReq);

        // Act
        using var patchReq = ReqWithBody(new HttpMethod("PATCH"), $"/api/v1/entries/income/{entryId}", uid,
            new { plannedAmount = 6000m });
        using var patchResp = await Client.SendAsync(patchReq);

        // Assert
        Assert.That(patchResp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // --- POST /api/entries/income/{id}/receive ---

    [Test]
    public async Task ReceiveIncomeEntry_SetsActualToPlannedWhenOmitted()
    {
        // Arrange
        var uid = Uid("receive-income-default");
        await CreateRecurringIncomeAsync(uid);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetIncomeEntryIdAsync(uid, 2025, 3);

        // Act
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/income/{entryId}/receive", uid, new { });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.Received, Is.True);
            Assert.That(body.ActualAmount, Is.EqualTo(5000m));
        });
    }

    // --- POST /api/entries/income/{id}/unreceive ---

    [Test]
    public async Task UnreceiveIncomeEntry_FreezeThenUnfreeze_AllowsEdit()
    {
        // Arrange
        var uid = Uid("unreceive-income");
        await CreateRecurringIncomeAsync(uid);
        await PostProjectionAsync(uid, 2025);
        var entryId = await GetIncomeEntryIdAsync(uid, 2025, 4);

        // Receive → freeze
        using var recvReq = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/income/{entryId}/receive", uid, new { });
        await Client.SendAsync(recvReq);

        // Unreceive → unfreeze
        using var unrecvReq = Req(HttpMethod.Post, $"/api/v1/entries/income/{entryId}/unreceive", uid);
        using var unrecvResp = await Client.SendAsync(unrecvReq);
        Assert.That(unrecvResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await unrecvResp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That(body!.Received, Is.False);

        // PATCH should now succeed
        using var patchReq = ReqWithBody(new HttpMethod("PATCH"), $"/api/v1/entries/income/{entryId}", uid,
            new { plannedAmount = 5100m });
        using var patchResp = await Client.SendAsync(patchReq);
        Assert.That(patchResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // --- Owner isolation ---

    [Test]
    public async Task PayBillEntry_EntryBelongingToAnotherOwner_Returns404()
    {
        // Arrange
        var uidA = Uid("owner-a-pay");
        var uidB = Uid("owner-b-pay");
        var catId = await GetFirstCategoryIdAsync(uidA);
        await CreateRecurringBillAsync(uidA, catId);
        await PostProjectionAsync(uidA, 2025);
        var entryId = await GetBillEntryIdAsync(uidA, 2025, 6);

        _ = await GetFirstCategoryIdAsync(uidB); // provision B

        // Act — B tries to pay A's entry
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uidB, new { });
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Auth ---

    [Test]
    public async Task PayBillEntry_WithoutToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries/bill/1/pay");
        req.Content = JsonContent.Create(new { });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task PatchBillEntry_WithoutToken_Returns401()
    {
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), "/api/v1/entries/bill/1");
        req.Content = JsonContent.Create(new { plannedAmount = 100m });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Local DTOs ---

    private sealed record EntryResponse(
        long Id, decimal PlannedAmount, decimal? ActualAmount,
        bool Paid, DateTimeOffset? PaidDate,
        bool Received, DateTimeOffset? ReceivedDate);

    private sealed record MonthEntriesResponse(
        int Year, int Month, BillDto2[] Bills, IncomeDto2[] Incomes);

    private sealed record BillDto2(long Id, decimal PlannedAmount, bool Paid);
    private sealed record IncomeDto2(long Id, decimal PlannedAmount, bool Received);
    private sealed record BillDto(long Id, string Name);
    private sealed record IncomeDto(long Id, string Name);
    private sealed record CategoryDto(long Id, string Name);
}
