using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/dashboard/year</c>, covering the 12-month series,
/// per-category yearly breakdown, grand totals, owner isolation, validation, and authentication.
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

    private async Task<BillDto> CreateOneOffBillAsync(string uid, long categoryId, string name, decimal amount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name, categoryId, kind = "one_off", defaultAmount = amount, splitRatio = 1m, personId = (long?)null });
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

    // --- Validation ---

    [TestCase(1999)]
    [TestCase(2101)]
    public async Task Get_YearOutOfRange_ReturnsBadRequest(int invalidYear)
    {
        // Arrange
        var uid = Uid($"bad-year-{invalidYear}");

        // Act
        var (status, _) = await GetDashboardYearAsync(uid, invalidYear);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Get_MissingYear_ReturnsBadRequest()
    {
        // Arrange
        var uid = Uid("missing-year");

        // Act
        var (status, _) = await GetDashboardYearAsync(uid, year: null);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
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

    // --- Local helpers / DTOs for JSON deserialization ---

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record BillEntryResponse(long Id, long BillId, int RefYear, int RefMonth);

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
