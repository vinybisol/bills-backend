using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/dashboard/month</c>, covering the month summary,
/// per-category breakdown, ordering, owner isolation, and authentication.
/// </summary>
[TestFixture]
public sealed class DashboardMonthEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-dashboard-{suffix}";

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

    private async Task<CategoryDto[]> GetCategoriesAsync(string uid)
    {
        using var req = Req(HttpMethod.Get, "/api/v1/categories", uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos!;
    }

    private async Task<BillDto> CreateOneOffBillAsync(
        string uid, long categoryId, string name, decimal amount, decimal splitRatio = 1m, long? personId = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name, categoryId, kind = "one_off", defaultAmount = amount, splitRatio, personId });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    private async Task<IncomeDto> CreateOneOffIncomeAsync(string uid, string name, decimal amount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name, kind = "one_off", defaultAmount = amount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    private async Task<long> CreateBillEntryAsync(string uid, long billId, int year, int month, decimal plannedAmount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/entries/bill", uid,
            new { billId, year, month, plannedAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillEntryResponse>())!.Id;
    }

    private async Task<long> CreateIncomeEntryAsync(string uid, long incomeId, int year, int month, decimal plannedAmount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/entries/income", uid,
            new { incomeId, year, month, plannedAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IncomeEntryResponse>())!.Id;
    }

    private async Task PayBillEntryAsync(string uid, long entryId, decimal? actualAmount = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { actualAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task ReceiveIncomeEntryAsync(string uid, long entryId, decimal? actualAmount = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/income/{entryId}/receive", uid, new { actualAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // Marks a bill entry's split as received (reimbursement) via POST /api/receivables/{entryId}/mark.
    private async Task MarkReceivedAsync(string uid, long entryId)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/receivables/{entryId}/mark", uid, new { receivedDate = (DateOnly?)null });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<(HttpStatusCode Status, DashboardMonthResponse? Body)> GetDashboardAsync(string uid, int? year, int? month)
    {
        var query = $"?{(year.HasValue ? $"year={year}" : "")}{(month.HasValue ? $"&month={month}" : "")}";
        using var req = Req(HttpMethod.Get, $"/api/v1/dashboard/month{query}", uid);
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<DashboardMonthResponse>() : null;
        return (resp.StatusCode, body);
    }

    // --- Summary + per-category breakdown ---

    [Test]
    public async Task Get_MonthWithEntries_ReturnsSummaryAndByCategory()
    {
        // Arrange
        var uid = Uid("summary");
        var categories = await GetCategoriesAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, categories[0].Id, "IPVA", 1000m, splitRatio: 0.5m,
            personId: await CreatePersonAsync(uid));
        var income = await CreateOneOffIncomeAsync(uid, "Freela", 2000m);

        var billEntryId = await CreateBillEntryAsync(uid, bill.Id, 2026, 3, 1000m);
        var incomeEntryId = await CreateIncomeEntryAsync(uid, income.Id, 2026, 3, 2000m);

        await PayBillEntryAsync(uid, billEntryId, actualAmount: 900m);
        await ReceiveIncomeEntryAsync(uid, incomeEntryId, actualAmount: 2100m);

        // Act
        var (status, body) = await GetDashboardAsync(uid, 2026, 3);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Year, Is.EqualTo(2026));
            Assert.That(body.Month, Is.EqualTo(3));
            // planned my share = 1000 x 0.5 = 500; actual my share (paid) = 900 x 0.5 = 450
            Assert.That(body.Summary.PlannedExpense, Is.EqualTo(500m));
            Assert.That(body.Summary.ActualExpense, Is.EqualTo(450m));
            Assert.That(body.Summary.PlannedIncome, Is.EqualTo(2000m));
            Assert.That(body.Summary.ActualIncome, Is.EqualTo(2100m));
            Assert.That(body.Summary.SaldoPrevisto, Is.EqualTo(1500m)); // 2000 - 500
            // SaldoReal is now an alias of SaldoRealizado: (received income + received reimbursements) minus
            // the FULL paid amount (900, not myShare 450) — the split half never got reimbursed here.
            // 2100 + 0 - 900 = 1200 (was 1650 under the old myShare-of-paid semantics).
            Assert.That(body.Summary.SaldoReal, Is.EqualTo(1200m));
            Assert.That(body.Summary.BillsPaid, Is.EqualTo(1));
            Assert.That(body.Summary.BillsTotal, Is.EqualTo(1));
            Assert.That(body.Summary.IncomesReceived, Is.EqualTo(1));
            Assert.That(body.Summary.IncomesTotal, Is.EqualTo(1));

            // receivable: bill effective=900, split=0.5 -> receivable=450, not yet received.
            Assert.That(body.Summary.ReceivablePending, Is.EqualTo(450m));
            Assert.That(body.Summary.ReceivableReceived, Is.EqualTo(0m));
            Assert.That(body.Summary.PaidFull, Is.EqualTo(900m));
            Assert.That(body.Summary.SaldoPrevistoOtimista, Is.EqualTo(body.Summary.SaldoPrevisto));
            Assert.That(body.Summary.SaldoPrevistoPiorCaso, Is.EqualTo(1050m)); // 1500 - 450
            Assert.That(body.Summary.SaldoRealizado, Is.EqualTo(body.Summary.SaldoReal));

            Assert.That(body.ByCategory, Has.Length.EqualTo(1));
            Assert.That(body.ByCategory[0].CategoryId, Is.EqualTo(categories[0].Id));
            Assert.That(body.ByCategory[0].PlannedMyShare, Is.EqualTo(500m));
            Assert.That(body.ByCategory[0].ActualMyShare, Is.EqualTo(450m));
            Assert.That(body.ByCategory[0].Diff, Is.EqualTo(-50m));
        });
    }

    [Test]
    public async Task Get_ThreeBalances_ComputeCorrectlyAndSatisfyGapInvariant()
    {
        // Arrange — three bills spanning the split spectrum, two incomes, in a distinct month.
        // Aluguel: split=1.0 (fully mine), planned=1000, paid in full.
        // Internet: split=0.5 (shared), planned=800, paid in full, reimbursement already received.
        // Presente: split=0.0 (passes through me), planned=500, unpaid, unreceived.
        var uid = Uid("three-balances");
        var categories = await GetCategoriesAsync(uid);
        var personId = await CreatePersonAsync(uid, name: "Parceiro");
        var aluguel = await CreateOneOffBillAsync(uid, categories[0].Id, "Aluguel", 1000m, splitRatio: 1.0m);
        var internet = await CreateOneOffBillAsync(uid, categories[0].Id, "Internet", 800m, splitRatio: 0.5m, personId: personId);
        var presente = await CreateOneOffBillAsync(uid, categories[0].Id, "Presente", 500m, splitRatio: 0.0m, personId: personId);
        var salario = await CreateOneOffIncomeAsync(uid, "Salario", 5000m);
        var freela = await CreateOneOffIncomeAsync(uid, "Freela", 1000m);

        var aluguelEntryId = await CreateBillEntryAsync(uid, aluguel.Id, 2026, 6, 1000m);
        var internetEntryId = await CreateBillEntryAsync(uid, internet.Id, 2026, 6, 800m);
        await CreateBillEntryAsync(uid, presente.Id, 2026, 6, 500m);
        var salarioEntryId = await CreateIncomeEntryAsync(uid, salario.Id, 2026, 6, 5000m);
        await CreateIncomeEntryAsync(uid, freela.Id, 2026, 6, 1000m);

        await PayBillEntryAsync(uid, aluguelEntryId, actualAmount: 1000m);
        await PayBillEntryAsync(uid, internetEntryId, actualAmount: 800m);
        await MarkReceivedAsync(uid, internetEntryId);
        await ReceiveIncomeEntryAsync(uid, salarioEntryId, actualAmount: 5200m);

        // Act
        var (status, body) = await GetDashboardAsync(uid, 2026, 6);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Summary.ReceivablePending, Is.EqualTo(500m)); // Presente: 500 x (1-0)
            Assert.That(body.Summary.ReceivableReceived, Is.EqualTo(400m)); // Internet: 800 x (1-0.5)
            Assert.That(body.Summary.PaidFull, Is.EqualTo(1800m)); // full value of Aluguel + Internet
            Assert.That(body.Summary.SaldoPrevistoOtimista, Is.EqualTo(4600m)); // 6000 - (1000 + 400 + 0)
            Assert.That(body.Summary.SaldoPrevistoPiorCaso, Is.EqualTo(4100m)); // 4600 - 500
            Assert.That(body.Summary.SaldoRealizado, Is.EqualTo(3800m)); // (5200 + 400) - 1800
            Assert.That(body.Summary.SaldoPrevisto, Is.EqualTo(body.Summary.SaldoPrevistoOtimista));
            Assert.That(body.Summary.SaldoReal, Is.EqualTo(body.Summary.SaldoRealizado));
            Assert.That(
                body.Summary.SaldoPrevistoOtimista - body.Summary.SaldoPrevistoPiorCaso,
                Is.EqualTo(body.Summary.ReceivablePending));
        });
    }

    [Test]
    public async Task Get_MonthWithZeroEntries_ReturnsZeroedStructure()
    {
        // Arrange — provision the user but create no entries for the month
        var uid = Uid("empty-month");
        await GetCategoriesAsync(uid);

        // Act
        var (status, body) = await GetDashboardAsync(uid, 2026, 11);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Summary.PlannedExpense, Is.EqualTo(0m));
            Assert.That(body.Summary.ActualExpense, Is.EqualTo(0m));
            Assert.That(body.Summary.PlannedIncome, Is.EqualTo(0m));
            Assert.That(body.Summary.ActualIncome, Is.EqualTo(0m));
            Assert.That(body.Summary.SaldoPrevisto, Is.EqualTo(0m));
            Assert.That(body.Summary.SaldoReal, Is.EqualTo(0m));
            Assert.That(body.Summary.BillsTotal, Is.EqualTo(0));
            Assert.That(body.Summary.IncomesTotal, Is.EqualTo(0));
            Assert.That(body.ByCategory, Is.Empty);
        });
    }

    [Test]
    public async Task Get_CategoryOrdering_IsDescendingByPlannedMyShare()
    {
        // Arrange — two categories with different planned amounts, in a distinct month
        var uid = Uid("ordering");
        var categories = await GetCategoriesAsync(uid);
        var smallBill = await CreateOneOffBillAsync(uid, categories[0].Id, "Internet", 100m);
        var largeBill = await CreateOneOffBillAsync(uid, categories[1].Id, "Aluguel", 900m);

        await CreateBillEntryAsync(uid, smallBill.Id, 2026, 4, 100m);
        await CreateBillEntryAsync(uid, largeBill.Id, 2026, 4, 900m);

        // Act
        var (status, body) = await GetDashboardAsync(uid, 2026, 4);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.ByCategory, Has.Length.EqualTo(2));
            Assert.That(body.ByCategory[0].CategoryId, Is.EqualTo(categories[1].Id));
            Assert.That(body.ByCategory[0].PlannedMyShare, Is.EqualTo(900m));
            Assert.That(body.ByCategory[1].CategoryId, Is.EqualTo(categories[0].Id));
            Assert.That(body.ByCategory[1].PlannedMyShare, Is.EqualTo(100m));
        });
    }

    // --- Owner isolation ---

    [Test]
    public async Task Get_OwnerIsolation_OnlyReturnsAuthenticatedOwnersEntries()
    {
        // Arrange — owner A has an entry in the month; owner B has none
        var uidA = Uid("isolate-a");
        var uidB = Uid("isolate-b");
        var categoriesA = await GetCategoriesAsync(uidA);
        var billA = await CreateOneOffBillAsync(uidA, categoriesA[0].Id, "Agua", 200m);
        await CreateBillEntryAsync(uidA, billA.Id, 2026, 5, 200m);

        await GetCategoriesAsync(uidB); // provision B

        // Act
        var (statusA, bodyA) = await GetDashboardAsync(uidA, 2026, 5);
        var (statusB, bodyB) = await GetDashboardAsync(uidB, 2026, 5);

        // Assert
        Assert.That(statusA, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(statusB, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(bodyA!.Summary.BillsTotal, Is.EqualTo(1));
            Assert.That(bodyA.Summary.PlannedExpense, Is.EqualTo(200m));
            Assert.That(bodyB!.Summary.BillsTotal, Is.EqualTo(0));
            Assert.That(bodyB.ByCategory, Is.Empty);
        });
    }

    // --- Validation ---

    [TestCase(0)]
    [TestCase(13)]
    public async Task Get_MonthOutOfRange_ReturnsBadRequest(int invalidMonth)
    {
        // Arrange
        var uid = Uid($"bad-month-{invalidMonth}");

        // Act
        var (status, _) = await GetDashboardAsync(uid, 2026, invalidMonth);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Get_MissingYearOrMonth_ReturnsBadRequest()
    {
        // Arrange
        var uid = Uid("missing-params");

        // Act
        var (status, _) = await GetDashboardAsync(uid, year: null, month: 5);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // --- Auth ---

    [Test]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard/month?year=2026&month=1");

        // Act
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Local helpers / DTOs for JSON deserialization ---

    private async Task<long> CreatePersonAsync(string uid, string name = "Esposa")
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
    private sealed record BillEntryResponse(long Id, long BillId, int RefYear, int RefMonth);
    private sealed record IncomeEntryResponse(long Id, long IncomeId, int RefYear, int RefMonth);

    private sealed record DashboardCategoryResponse(long CategoryId, string Category, decimal PlannedMyShare, decimal ActualMyShare, decimal Diff);

    private sealed record DashboardSummaryResponse(
        decimal PlannedExpense, decimal ActualExpense,
        decimal PlannedIncome, decimal ActualIncome,
        decimal SaldoPrevisto, decimal SaldoReal,
        int BillsPaid, int BillsTotal,
        int IncomesReceived, int IncomesTotal,
        decimal ReceivablePending, decimal ReceivableReceived, decimal PaidFull,
        decimal SaldoPrevistoOtimista, decimal SaldoPrevistoPiorCaso, decimal SaldoRealizado);

    private sealed record DashboardMonthResponse(int Year, int Month, DashboardSummaryResponse Summary, DashboardCategoryResponse[] ByCategory);
}
