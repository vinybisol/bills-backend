using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Api.IntegrationTests.TestSupport.ProblemAssertions;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for <c>GET /api/v1/dashboard/year</c>, covering the 12-month series,
/// per-category yearly breakdown, grand totals, owner isolation, deactivated templates, validation, and authentication.
/// </summary>
[TestFixture]
public sealed class DashboardYearEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-dashboard-year-{suffix}";

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

    private async Task<long> CreateBillEntryAsync(string uid, long billId, int year, int month, decimal plannedAmount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/entries/bill", uid,
            new { billId, year, month, plannedAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillEntryResponse>())!.Id;
    }

    private async Task PayBillEntryAsync(string uid, long entryId, decimal? actualAmount = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { actualAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<long> CreatePersonAsync(string uid, string name = "Esposa")
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<long> CreateOneOffIncomeAsync(string uid, string name, decimal amount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name, kind = "one_off", defaultAmount = amount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<long> CreateIncomeEntryAsync(string uid, long incomeId, int year, int month, decimal plannedAmount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/entries/income", uid,
            new { incomeId, year, month, plannedAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task ReceiveIncomeEntryAsync(string uid, long entryId, decimal? actualAmount = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/income/{entryId}/receive", uid, new { actualAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<(HttpStatusCode Status, DashboardYearResponse? Body)> GetDashboardYearAsync(string uid, int? year)
    {
        var query = year.HasValue ? $"?year={year}" : "";
        using var req = Req(HttpMethod.Get, $"/api/v1/dashboard/year{query}", uid);
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<DashboardYearResponse>() : null;
        return (resp.StatusCode, body);
    }

    // --- Full year with entries ---

    [Test]
    public async Task Get_YearWithEntriesInAFewMonths_Returns12MonthsWithNonPopulatedOnesZeroed()
    {
        // Arrange
        var uid = Uid("some-months");
        var categories = await GetCategoriesAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, categories[0].Id, "Internet", 100m);

        var janEntry = await CreateBillEntryAsync(uid, bill.Id, 2030, 1, 100m);
        await PayBillEntryAsync(uid, janEntry, actualAmount: 90m);
        await CreateBillEntryAsync(uid, bill.Id, 2030, 7, 200m);

        // Act
        var (status, body) = await GetDashboardYearAsync(uid, 2030);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Year, Is.EqualTo(2030));
            Assert.That(body.Months, Has.Length.EqualTo(12));
            Assert.That(body.Months.Select(m => m.Month), Is.EqualTo(Enumerable.Range(1, 12)));

            var jan = body.Months[0];
            Assert.That(jan.PlannedExpense, Is.EqualTo(100m));
            Assert.That(jan.ActualExpense, Is.EqualTo(90m));

            var jul = body.Months[6];
            Assert.That(jul.PlannedExpense, Is.EqualTo(200m));
            Assert.That(jul.ActualExpense, Is.EqualTo(0m));

            // All other months are fully zeroed.
            var others = body.Months.Where(m => m.Month != 1 && m.Month != 7);
            Assert.That(others.All(m => m.PlannedExpense == 0m && m.ActualExpense == 0m
                && m.PlannedIncome == 0m && m.ActualIncome == 0m), Is.True);
        });
    }

    [Test]
    public async Task Get_YearWithEntries_ByCategoryTotalsSumWholeYear()
    {
        // Arrange — two entries, same category, different months
        var uid = Uid("category-totals");
        var categories = await GetCategoriesAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, categories[0].Id, "Agua", 50m);
        await CreateBillEntryAsync(uid, bill.Id, 2031, 2, 50m);
        await CreateBillEntryAsync(uid, bill.Id, 2031, 9, 75m);

        // Act
        var (status, body) = await GetDashboardYearAsync(uid, 2031);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.ByCategory, Has.Length.EqualTo(1));
            Assert.That(body.ByCategory[0].CategoryId, Is.EqualTo(categories[0].Id));
            Assert.That(body.ByCategory[0].PlannedMyShare, Is.EqualTo(125m)); // 50 + 75
        });
    }

    [Test]
    public async Task Get_YearWithEntries_GrandTotalsEqualSumOfTwelveMonths()
    {
        // Arrange
        var uid = Uid("grand-totals");
        var categories = await GetCategoriesAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, categories[0].Id, "Streaming", 40m);
        await CreateBillEntryAsync(uid, bill.Id, 2032, 3, 40m);
        await CreateBillEntryAsync(uid, bill.Id, 2032, 11, 60m);

        // Act
        var (status, body) = await GetDashboardYearAsync(uid, 2032);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Totals.PlannedExpense, Is.EqualTo(body.Months.Sum(m => m.PlannedExpense)));
            Assert.That(body.Totals.ActualExpense, Is.EqualTo(body.Months.Sum(m => m.ActualExpense)));
            Assert.That(body.Totals.PlannedIncome, Is.EqualTo(body.Months.Sum(m => m.PlannedIncome)));
            Assert.That(body.Totals.ActualIncome, Is.EqualTo(body.Months.Sum(m => m.ActualIncome)));
            Assert.That(body.Totals.PlannedExpense, Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task Get_SplitBillAndIncome_MonthUsesMyShareAndReceivedIncome()
    {
        // Arrange — split bill (half is receivable, not my expense) + income received above plan.
        var uid = Uid("split-income");
        var categories = await GetCategoriesAsync(uid);
        var personId = await CreatePersonAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, categories[0].Id, "Aluguel", 1000m, splitRatio: 0.5m, personId: personId);
        var billEntry = await CreateBillEntryAsync(uid, bill.Id, 2037, 5, 1000m);
        await PayBillEntryAsync(uid, billEntry, actualAmount: 900m);
        var income = await CreateOneOffIncomeAsync(uid, "Salario", 3000m);
        var incomeEntry = await CreateIncomeEntryAsync(uid, income, 2037, 5, 3000m);
        await ReceiveIncomeEntryAsync(uid, incomeEntry, actualAmount: 3100m);
        await CreateIncomeEntryAsync(uid, income, 2037, 6, 3000m);

        // Act
        var (status, body) = await GetDashboardYearAsync(uid, 2037);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Months[4], Is.EqualTo(new DashboardMonthSummaryResponse(5, 500m, 450m, 3000m, 3100m, 2500m, 2650m)));
            Assert.That(body.Months[5], Is.EqualTo(new DashboardMonthSummaryResponse(6, 0m, 0m, 3000m, 0m, 3000m, 0m)));
            Assert.That(body.ByCategory, Is.EqualTo(new[]
            {
                new DashboardCategoryYearResponse(categories[0].Id, categories[0].Name, 500m, 450m),
            }));
            Assert.That(body.Totals, Is.EqualTo(new DashboardYearTotalsResponse(500m, 450m, 6000m, 3100m, 5500m, 2650m)));
        });
    }

    // --- Empty year ---

    [Test]
    public async Task Get_EmptyYear_ReturnsZeroedStructure()
    {
        // Arrange — provision the user but create no entries for the year
        var uid = Uid("empty-year");
        await GetCategoriesAsync(uid);

        // Act
        var (status, body) = await GetDashboardYearAsync(uid, 2033);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Months, Has.Length.EqualTo(12));
            Assert.That(body.Months.All(m => m.PlannedExpense == 0m && m.ActualExpense == 0m
                && m.PlannedIncome == 0m && m.ActualIncome == 0m
                && m.SaldoPrevisto == 0m && m.SaldoReal == 0m), Is.True);
            Assert.That(body.ByCategory, Is.Empty);
            Assert.That(body.Totals.PlannedExpense, Is.EqualTo(0m));
            Assert.That(body.Totals.SaldoReal, Is.EqualTo(0m));
        });
    }

    // --- Owner isolation ---

    [Test]
    public async Task Get_OwnerIsolation_OnlyReturnsAuthenticatedOwnersEntries()
    {
        // Arrange
        var uidA = Uid("isolate-a");
        var uidB = Uid("isolate-b");
        var categoriesA = await GetCategoriesAsync(uidA);
        var billA = await CreateOneOffBillAsync(uidA, categoriesA[0].Id, "Gas", 30m);
        await CreateBillEntryAsync(uidA, billA.Id, 2034, 4, 30m);

        await GetCategoriesAsync(uidB); // provision B

        // Act
        var (statusA, bodyA) = await GetDashboardYearAsync(uidA, 2034);
        var (statusB, bodyB) = await GetDashboardYearAsync(uidB, 2034);

        // Assert
        Assert.That(statusA, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(statusB, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(bodyA!.Totals.PlannedExpense, Is.EqualTo(30m));
            Assert.That(bodyB!.Totals.PlannedExpense, Is.EqualTo(0m));
            Assert.That(bodyB.ByCategory, Is.Empty);
        });
    }

    [Test]
    public async Task Get_OtherOwnerHasEntriesInSameYear_DoesNotLeakIntoMonthsOrCategories()
    {
        // Arrange — both owners have data in the year; each must only see their own.
        var uidA = Uid("isolate-both-a");
        var uidB = Uid("isolate-both-b");
        var categoriesA = await GetCategoriesAsync(uidA);
        var categoriesB = await GetCategoriesAsync(uidB);
        var billA = await CreateOneOffBillAsync(uidA, categoriesA[0].Id, "Gas", 30m);
        var billB = await CreateOneOffBillAsync(uidB, categoriesB[0].Id, "Gas", 900m);
        await CreateBillEntryAsync(uidA, billA.Id, 2035, 4, 30m);
        await CreateBillEntryAsync(uidB, billB.Id, 2035, 4, 900m);
        await CreateBillEntryAsync(uidB, billB.Id, 2035, 5, 900m);

        // Act
        var (status, body) = await GetDashboardYearAsync(uidA, 2035);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Months[3].PlannedExpense, Is.EqualTo(30m));
            Assert.That(body.Months[4].PlannedExpense, Is.EqualTo(0m));
            Assert.That(body.Totals.PlannedExpense, Is.EqualTo(30m));
            Assert.That(body.ByCategory.Select(c => c.CategoryId), Is.EqualTo(new[] { categoriesA[0].Id }));
        });
    }

    // --- Deactivated templates ---

    [Test]
    public async Task Get_DeactivatedBillAndCategory_StillCountedAndNamed()
    {
        // Arrange
        var uid = Uid("deactivated");
        var categories = await GetCategoriesAsync(uid);
        var bill = await CreateOneOffBillAsync(uid, categories[0].Id, "Academia", 120m);
        await CreateBillEntryAsync(uid, bill.Id, 2036, 2, 120m);
        await DeleteAsync(uid, $"/api/v1/bills/{bill.Id}");
        await DeleteAsync(uid, $"/api/v1/categories/{categories[0].Id}");

        // Act
        var (status, body) = await GetDashboardYearAsync(uid, 2036);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Months[1].PlannedExpense, Is.EqualTo(120m));
            Assert.That(body.ByCategory, Has.Length.EqualTo(1));
            Assert.That((body.ByCategory[0].CategoryId, body.ByCategory[0].Category),
                Is.EqualTo((categories[0].Id, categories[0].Name)));
        });
    }

    // --- Validation ---

    [TestCase("")]
    [TestCase("year=1999")]
    [TestCase("year=2101")]
    public async Task Get_InvalidYear_ReturnsValidationProblem(string query)
    {
        // Arrange
        var uid = NewFirebaseUid();

        // Act
        using var req = Req(HttpMethod.Get, $"/api/v1/dashboard/year?{query}", uid);
        using var resp = await Client.SendAsync(req);

        // Assert
        await AssertValidationProblemAsync(resp, "year");
    }

    // --- Auth ---

    [Test]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard/year?year=2030");

        // Act
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Get_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard/year?year=2030")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt") }
        };

        // Act
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Local helpers / DTOs for JSON deserialization ---

    private async Task DeleteAsync(string uid, string url)
    {
        using var req = Req(HttpMethod.Delete, url, uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record BillEntryResponse(long Id, long BillId, int RefYear, int RefMonth);
    private sealed record IdResponse(long Id);

    private sealed record DashboardMonthSummaryResponse(
        int Month, decimal PlannedExpense, decimal ActualExpense,
        decimal PlannedIncome, decimal ActualIncome, decimal SaldoPrevisto, decimal SaldoReal);

    private sealed record DashboardCategoryYearResponse(long CategoryId, string Category, decimal PlannedMyShare, decimal ActualMyShare);

    private sealed record DashboardYearTotalsResponse(
        decimal PlannedExpense, decimal ActualExpense,
        decimal PlannedIncome, decimal ActualIncome, decimal SaldoPrevisto, decimal SaldoReal);

    private sealed record DashboardYearResponse(
        int Year, DashboardMonthSummaryResponse[] Months,
        DashboardCategoryYearResponse[] ByCategory, DashboardYearTotalsResponse Totals);
}
