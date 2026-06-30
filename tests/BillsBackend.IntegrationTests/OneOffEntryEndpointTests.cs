using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillsBackend.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for <c>POST /api/entries/bill</c>, <c>POST /api/entries/income</c>,
/// <c>DELETE /api/entries/bill/{id}</c>, and <c>DELETE /api/entries/income/{id}</c>.
/// Covers one-off entry creation with snapshots, UNIQUE enforcement, owner isolation,
/// immutability (paid/received) on deletion, and authentication.
/// </summary>
[TestFixture]
public sealed class OneOffEntryEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-oneoff-{suffix}";

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
        using var req = Req(HttpMethod.Get, "/categories", uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos![0].Id;
    }

    private async Task<BillDto> CreateOneOffBillAsync(string uid, long categoryId,
        string name = "IPVA", decimal amount = 1200m, decimal splitRatio = 1m, long? personId = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/bills", uid,
            new { name, categoryId, kind = "one_off", defaultAmount = amount, splitRatio, personId });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    private async Task<BillDto> CreateRecurringBillAsync(string uid, long categoryId, string name = "Aluguel")
    {
        using var req = ReqWithBody(HttpMethod.Post, "/bills", uid,
            new { name, categoryId, kind = "recurring", defaultAmount = 1000m, splitRatio = 1m, personId = (long?)null });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    private async Task<IncomeDto> CreateOneOffIncomeAsync(string uid, string name = "Reembolso", decimal amount = 500m)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/incomes", uid,
            new { name, kind = "one_off", defaultAmount = amount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    private async Task<long> CreatePersonAsync(string uid, string name = "Parceiro")
    {
        using var req = ReqWithBody(HttpMethod.Post, "/persons", uid, new { name });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    private async Task<(HttpResponseMessage Response, BillEntryResponse? Body)> PostBillEntryAsync(
        string uid, long billId, int year, int month, decimal? plannedAmount = null)
    {
        var body = new { billId, year, month, plannedAmount };
        using var req = ReqWithBody(HttpMethod.Post, "/api/entries/bill", uid, body);
        var resp = await Client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return (resp, null);
        var dto = await resp.Content.ReadFromJsonAsync<BillEntryResponse>();
        return (resp, dto);
    }

    private async Task<(HttpResponseMessage Response, IncomeEntryResponse? Body)> PostIncomeEntryAsync(
        string uid, long incomeId, int year, int month, decimal? plannedAmount = null)
    {
        var body = new { incomeId, year, month, plannedAmount };
        using var req = ReqWithBody(HttpMethod.Post, "/api/entries/income", uid, body);
        var resp = await Client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return (resp, null);
        var dto = await resp.Content.ReadFromJsonAsync<IncomeEntryResponse>();
        return (resp, dto);
    }

    // --- Bill entry creation ---

    [Test]
    public async Task PostBillEntry_OneOffBill_Returns201WithSnapshots()
    {
        // Arrange
        var uid = Uid("create-bill");
        var catId = await GetFirstCategoryIdAsync(uid);
        var personId = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateOneOffBillAsync(uid, catId, name: "IPVA", amount: 1200m, splitRatio: 0.5m, personId: personId);

        // Act
        var (resp, body) = await PostBillEntryAsync(uid, bill.Id, 2026, 4, plannedAmount: 1200m);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.BillId, Is.EqualTo(bill.Id));
            Assert.That(body.PlannedAmount, Is.EqualTo(1200m));
            Assert.That(body.SplitRatioSnapshot, Is.EqualTo(0.5m));
            Assert.That(body.PersonId, Is.EqualTo(personId));
            Assert.That(body.Paid, Is.False);
            Assert.That(body.RefYear, Is.EqualTo(2026));
            Assert.That(body.RefMonth, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task PostBillEntry_UsesDefaultAmountWhenPlannedAmountOmitted()
    {
        // Arrange
        var uid = Uid("create-bill-default");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, catId, name: "Revisao", amount: 600m);

        // Act — plannedAmount is null, should fall back to bill.DefaultAmount
        var (resp, body) = await PostBillEntryAsync(uid, bill.Id, 2026, 5, plannedAmount: null);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(body!.PlannedAmount, Is.EqualTo(600m));
    }

    [Test]
    public async Task PostBillEntry_DuplicateBillMonth_Returns409()
    {
        // Arrange
        var uid = Uid("dup-bill");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, catId);
        var (firstResp, _) = await PostBillEntryAsync(uid, bill.Id, 2026, 6, 1200m);
        Assert.That(firstResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Act — same bill, same month
        var (dupResp, _) = await PostBillEntryAsync(uid, bill.Id, 2026, 6, 1200m);

        // Assert
        Assert.That(dupResp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task PostBillEntry_RecurringBill_Returns400()
    {
        // Arrange — recurring bills cannot be used via one-off entry endpoint
        var uid = Uid("recurring-bill");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateRecurringBillAsync(uid, catId);

        // Act
        var (resp, _) = await PostBillEntryAsync(uid, bill.Id, 2026, 7, 1000m);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostBillEntry_BillBelongingToAnotherOwner_Returns404()
    {
        // Arrange — owner A creates the bill
        var uidA = Uid("owner-a-bill");
        var uidB = Uid("owner-b-bill");
        var catIdA = await GetFirstCategoryIdAsync(uidA);
        var billA = await CreateOneOffBillAsync(uidA, catIdA);

        // Act — owner B tries to create an entry for owner A's bill
        _ = await GetFirstCategoryIdAsync(uidB); // provision B
        var (resp, _) = await PostBillEntryAsync(uidB, billA.Id, 2026, 8, 1200m);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase(0)]
    [TestCase(13)]
    public async Task PostBillEntry_InvalidMonth_Returns400(int invalidMonth)
    {
        var uid = Uid($"inv-month-bill-{invalidMonth}");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, catId);

        var (resp, _) = await PostBillEntryAsync(uid, bill.Id, 2026, invalidMonth, 100m);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostBillEntry_NegativePlannedAmount_Returns400()
    {
        var uid = Uid("neg-amount-bill");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, catId);

        var (resp, _) = await PostBillEntryAsync(uid, bill.Id, 2026, 1, -1m);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostBillEntry_WithoutToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/entries/bill");
        req.Content = JsonContent.Create(new { billId = 1, year = 2026, month = 1 });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Bill entry deletion ---

    [Test]
    public async Task DeleteBillEntry_UnpaidEntry_Returns204()
    {
        // Arrange
        var uid = Uid("delete-bill-unpaid");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, catId);
        var (createResp, body) = await PostBillEntryAsync(uid, bill.Id, 2026, 9, 1200m);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Act
        using var delReq = Req(HttpMethod.Delete, $"/api/entries/bill/{body!.Id}", uid);
        using var delResp = await Client.SendAsync(delReq);

        // Assert
        Assert.That(delResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteBillEntry_PaidEntry_Returns409()
    {
        // Arrange — create entry then mark it paid directly in DB
        // (the /pay endpoint belongs to issue #20; here we bypass it for isolation)
        var uid = Uid("delete-bill-paid");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, catId);
        var (createResp, body) = await PostBillEntryAsync(uid, bill.Id, 2026, 10, 1200m);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Mark paid directly in DB
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.BillEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == body!.Id);
            entry.MarkPaid(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // Act — try to delete a paid entry
        using var delReq = Req(HttpMethod.Delete, $"/api/entries/bill/{body!.Id}", uid);
        using var delResp = await Client.SendAsync(delReq);

        // Assert
        Assert.That(delResp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task DeleteBillEntry_WithoutToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, "/api/entries/bill/1");
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Income entry creation ---

    [Test]
    public async Task PostIncomeEntry_OneOffIncome_Returns201WithSnapshots()
    {
        // Arrange
        var uid = Uid("create-income");
        var income = await CreateOneOffIncomeAsync(uid, name: "Reembolso", amount: 500m);

        // Act
        var (resp, body) = await PostIncomeEntryAsync(uid, income.Id, 2026, 4, plannedAmount: 500m);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.IncomeId, Is.EqualTo(income.Id));
            Assert.That(body.PlannedAmount, Is.EqualTo(500m));
            Assert.That(body.Received, Is.False);
            Assert.That(body.RefYear, Is.EqualTo(2026));
            Assert.That(body.RefMonth, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task PostIncomeEntry_DuplicateIncomeMonth_Returns409()
    {
        // Arrange
        var uid = Uid("dup-income");
        var income = await CreateOneOffIncomeAsync(uid);
        var (firstResp, _) = await PostIncomeEntryAsync(uid, income.Id, 2026, 6, 500m);
        Assert.That(firstResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Act
        var (dupResp, _) = await PostIncomeEntryAsync(uid, income.Id, 2026, 6, 500m);

        // Assert
        Assert.That(dupResp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task PostIncomeEntry_IncomeBelongingToAnotherOwner_Returns404()
    {
        // Arrange
        var uidA = Uid("owner-a-income");
        var uidB = Uid("owner-b-income");
        var incomeA = await CreateOneOffIncomeAsync(uidA);

        _ = await GetFirstCategoryIdAsync(uidB); // provision B
        var (resp, _) = await PostIncomeEntryAsync(uidB, incomeA.Id, 2026, 8, 500m);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PostIncomeEntry_WithoutToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/entries/income");
        req.Content = JsonContent.Create(new { incomeId = 1, year = 2026, month = 1 });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Income entry deletion ---

    [Test]
    public async Task DeleteIncomeEntry_UnreceivedEntry_Returns204()
    {
        // Arrange
        var uid = Uid("delete-income-unreceived");
        var income = await CreateOneOffIncomeAsync(uid);
        var (createResp, body) = await PostIncomeEntryAsync(uid, income.Id, 2026, 9, 500m);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Act
        using var delReq = Req(HttpMethod.Delete, $"/api/entries/income/{body!.Id}", uid);
        using var delResp = await Client.SendAsync(delReq);

        // Assert
        Assert.That(delResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIncomeEntry_ReceivedEntry_Returns409()
    {
        // Arrange — create entry then mark it received directly in DB
        var uid = Uid("delete-income-received");
        var income = await CreateOneOffIncomeAsync(uid);
        var (createResp, body) = await PostIncomeEntryAsync(uid, income.Id, 2026, 10, 500m);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Mark received directly in DB
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.IncomeEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == body!.Id);
            entry.MarkReceived(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // Act
        using var delReq = Req(HttpMethod.Delete, $"/api/entries/income/{body!.Id}", uid);
        using var delResp = await Client.SendAsync(delReq);

        // Assert
        Assert.That(delResp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task DeleteIncomeEntry_WithoutToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, "/api/entries/income/1");
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Local DTOs ---

    private sealed record BillEntryResponse(
        long Id, long BillId, int RefYear, int RefMonth,
        decimal PlannedAmount, decimal? ActualAmount,
        decimal SplitRatioSnapshot, long? PersonId,
        bool Paid, DateTimeOffset? PaidDate,
        bool Received, DateTimeOffset? ReceivedDate);

    private sealed record IncomeEntryResponse(
        long Id, long IncomeId, int RefYear, int RefMonth,
        decimal PlannedAmount, decimal? ActualAmount,
        bool Received, DateTimeOffset? ReceivedDate);

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
}
