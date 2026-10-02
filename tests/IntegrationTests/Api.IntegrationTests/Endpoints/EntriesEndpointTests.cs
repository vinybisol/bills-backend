using System.Net;
using System.Net.Http.Json;
using static Api.IntegrationTests.TestSupport.ProblemAssertions;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for <c>GET /api/v1/entries</c>: entry enrichment with names (also for
/// deactivated templates), derived values, totals and balances, owner isolation, authentication and
/// query validation. Each test authenticates as a fresh Firebase uid.
/// </summary>
[TestFixture]
public sealed class EntriesEndpointTests : IntegrationTestBase
{
    private const string EntriesUri = "/api/v1/entries";

    // --- Listing ---

    [Test]
    public async Task Get_ValidYearMonth_ReturnsBillsAndIncomes()
    {
        // Arrange
        const int year = 2025;
        const int month = 1;
        using var client = CreateAuthenticatedClient();
        var categories = await GetDefaultCategoriesAsync(client);
        await CreateBillAsync(client, categories[0].Id, "Aluguel", 1500m);
        await CreateRecurringIncomeAsync(client, name: "Salario", amount: 5000m);
        await PostProjectionAsync(client, year);

        // Act
        var (resp, body) = await GetEntriesAsync(client, year, month);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Year, Is.EqualTo(year));
            Assert.That(body.Month, Is.EqualTo(month));
            Assert.That(body.Bills, Has.Length.EqualTo(1));
            Assert.That(body.Incomes, Has.Length.EqualTo(1));
        }

        var bill = body!.Bills[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bill.Name, Is.EqualTo("Aluguel"));
            Assert.That(bill.PlannedAmount, Is.EqualTo(1500m));
            Assert.That(bill.ActualAmount, Is.Null);
            Assert.That(bill.SplitRatio, Is.EqualTo(1m));
            Assert.That(bill.Person, Is.Null);
            Assert.That(bill.EffectiveAmount, Is.EqualTo(1500m));
            Assert.That(bill.MyShare, Is.EqualTo(1500m));
            Assert.That(bill.Receivable, Is.EqualTo(0m));
            Assert.That(bill.Paid, Is.False);
        }

        var income = body.Incomes[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(income.Name, Is.EqualTo("Salario"));
            Assert.That(income.PlannedAmount, Is.EqualTo(5000m));
            Assert.That(income.ActualAmount, Is.Null);
            Assert.That(income.EffectiveAmount, Is.EqualTo(5000m));
            Assert.That(income.Received, Is.False);
        }
    }

    [Test]
    public async Task Get_BillNames_CategoryAndPersonFilled()
    {
        // Arrange
        const int year = 2025;
        using var client = CreateAuthenticatedClient();
        var categories = await GetDefaultCategoriesAsync(client);
        var firstCategory = categories[0];
        var personId = await CreatePersonAsync(client, name: "Esposa");
        await CreateBillAsync(client, firstCategory.Id, "Internet", 200m, splitRatio: 0.5m, personId: personId);
        await PostProjectionAsync(client, year);

        // Act
        var (resp, body) = await GetEntriesAsync(client, year, 1);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Bills, Has.Length.EqualTo(1));
        var bill = body.Bills[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bill.Name, Is.EqualTo("Internet"));
            Assert.That(bill.Category, Is.EqualTo(firstCategory.Name));
            Assert.That(bill.Person, Is.EqualTo("Esposa"));
            Assert.That(bill.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(bill.EffectiveAmount, Is.EqualTo(200m));
            Assert.That(bill.MyShare, Is.EqualTo(100m));
            Assert.That(bill.Receivable, Is.EqualTo(100m));
        }
    }

    [Test]
    public async Task Get_DeactivatedTemplatesAndPerson_StillResolveNames()
    {
        // Arrange — soft-deleting moldes must not break the listing of their snapshotted entries
        const int year = 2025;
        using var client = CreateAuthenticatedClient();
        var categories = await GetDefaultCategoriesAsync(client);
        var personId = await CreatePersonAsync(client, name: "Esposa");
        var bill = await CreateBillAsync(client, categories[0].Id, "Internet", 200m, splitRatio: 0.5m, personId: personId);
        var income = await CreateRecurringIncomeAsync(client, name: "Salario", amount: 5000m);
        await PostProjectionAsync(client, year);
        await DeleteAsync(client, $"/api/v1/bills/{bill.Id}");
        await DeleteAsync(client, $"/api/v1/incomes/{income.Id}");
        await DeleteAsync(client, $"/api/v1/persons/{personId}");

        // Act
        var (resp, body) = await GetEntriesAsync(client, year, 1);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Bills.Select(b => (b.Name, b.Category, b.Person)),
                Is.EqualTo(new[] { ("Internet", categories[0].Name, (string?)"Esposa") }));
            Assert.That(body.Incomes.Select(i => i.Name), Is.EqualTo(new[] { "Salario" }));
        }
    }

    [Test]
    public async Task Get_Bills_SortedByCategoryThenName()
    {
        // Arrange
        const int year = 2025;
        using var client = CreateAuthenticatedClient();
        var categories = (await GetDefaultCategoriesAsync(client)).OrderBy(c => c.Name).ToArray();
        await CreateBillAsync(client, categories[1].Id, "Beta", 10m);
        await CreateBillAsync(client, categories[0].Id, "Zeta", 10m);
        await CreateBillAsync(client, categories[0].Id, "Alfa", 10m);
        await PostProjectionAsync(client, year);

        // Act
        var (_, body) = await GetEntriesAsync(client, year, 1);

        // Assert
        Assert.That(body!.Bills.Select(b => b.Name), Is.EqualTo(new[] { "Alfa", "Zeta", "Beta" }));
    }

    [Test]
    public async Task Get_NoEntries_Returns200WithEmptyListsAndZeroTotals()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        var (resp, body) = await GetEntriesAsync(client, 2025, 1);

        // Assert — the listing is an object (lists + totals), so it stays 200 even when empty
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Bills, Is.Empty);
            Assert.That(body.Incomes, Is.Empty);
            Assert.That(body.Totals.BillsPlanned, Is.Zero);
            Assert.That(body.Totals.IncomesPlanned, Is.Zero);
            Assert.That(body.Totals.SaldoRealizado, Is.Zero);
        }
    }

    // --- Totals ---

    [Test]
    public async Task Get_Totals_SplitsReceivableIntoPendingAndReceived()
    {
        // Arrange — two split bills; only one entry is marked as received
        const int year = 2025;
        const int month = 1;
        using var client = CreateAuthenticatedClient();
        var categories = await GetDefaultCategoriesAsync(client);
        var personId = await CreatePersonAsync(client, name: "Esposa");
        await CreateBillAsync(client, categories[0].Id, "Internet", 200m, splitRatio: 0.5m, personId: personId); // receivable 100
        await CreateBillAsync(client, categories[0].Id, "Streaming", 100m, splitRatio: 0.5m, personId: personId); // receivable 50
        await PostProjectionAsync(client, year);

        var (_, before) = await GetEntriesAsync(client, year, month);
        var streamingEntry = before!.Bills.Single(b => b.Name == "Streaming");
        await MarkReceivedAsync(client, streamingEntry.Id);

        // Act
        var (resp, body) = await GetEntriesAsync(client, year, month);

        // Assert — 50 already received (Streaming), 100 still pending (Internet); sum stays 150
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Totals.Received, Is.EqualTo(50m));
            Assert.That(body.Totals.Receivable, Is.EqualTo(100m));
        }
    }

    [Test]
    public async Task Get_Totals_ThreeBalances_ComputeCorrectlyAndSatisfyGapInvariant()
    {
        // Arrange — three bills spanning the split spectrum, two incomes, one already paid/received.
        // Aluguel: split=1.0 (fully mine), planned=1000, paid in full.
        // Internet: split=0.5 (shared), planned=800, paid in full, reimbursement already received.
        // Presente: split=0.0 (passes through me), planned=500, unpaid, unreceived.
        const int year = 2025;
        const int month = 1;
        using var client = CreateAuthenticatedClient();
        var categories = await GetDefaultCategoriesAsync(client);
        var personId = await CreatePersonAsync(client, name: "Parceiro");
        await CreateBillAsync(client, categories[0].Id, "Aluguel", 1000m);
        await CreateBillAsync(client, categories[0].Id, "Internet", 800m, splitRatio: 0.5m, personId: personId);
        await CreateBillAsync(client, categories[0].Id, "Presente", 500m, splitRatio: 0.0m, personId: personId);
        await CreateRecurringIncomeAsync(client, name: "Salario", amount: 5000m);
        await CreateRecurringIncomeAsync(client, name: "Freela", amount: 1000m);
        await PostProjectionAsync(client, year);

        var (_, before) = await GetEntriesAsync(client, year, month);
        var aluguel = before!.Bills.Single(b => b.Name == "Aluguel");
        var internet = before.Bills.Single(b => b.Name == "Internet");
        var salario = before.Incomes.Single(i => i.Name == "Salario");

        await PayBillEntryAsync(client, aluguel.Id, actualAmount: 1000m);
        await PayBillEntryAsync(client, internet.Id, actualAmount: 800m);
        await MarkReceivedAsync(client, internet.Id);
        await ReceiveIncomeEntryAsync(client, salario.Id, actualAmount: 5200m);

        // Act
        var (resp, body) = await GetEntriesAsync(client, year, month);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Totals.ReceivablePending, Is.EqualTo(500m)); // Presente: 500 x (1-0)
            Assert.That(body.Totals.ReceivableReceived, Is.EqualTo(400m)); // Internet: 800 x (1-0.5)
            Assert.That(body.Totals.PaidFull, Is.EqualTo(1800m)); // full value of Aluguel + Internet
            Assert.That(body.Totals.SaldoPrevistoOtimista, Is.EqualTo(4600m)); // 6000 - (1000 + 400 + 0)
            Assert.That(body.Totals.SaldoPrevistoPiorCaso, Is.EqualTo(4100m)); // 4600 - 500
            Assert.That(body.Totals.SaldoRealizado, Is.EqualTo(3800m)); // (5200 + 400) - 1800
            Assert.That(body.Totals.IncomesEffective, Is.EqualTo(6200m)); // 5200 (Salario) + 1000 (Freela, not received)
            Assert.That(body.Totals.IncomesReceived, Is.EqualTo(5200m)); // only Salario is received
            Assert.That(body.Totals.SaldoPrevisto, Is.EqualTo(body.Totals.SaldoPrevistoOtimista));
            Assert.That(body.Totals.SaldoReal, Is.EqualTo(body.Totals.SaldoRealizado));
            Assert.That(
                body.Totals.SaldoPrevistoOtimista - body.Totals.SaldoPrevistoPiorCaso,
                Is.EqualTo(body.Totals.ReceivablePending));
        }
    }

    // --- Owner isolation ---

    [Test]
    public async Task Get_OwnerIsolation_DoesNotSeeOtherUsersEntries()
    {
        // Arrange — user A creates bill + income and generates a projection
        const int year = 2025;
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var categoriesA = await GetDefaultCategoriesAsync(clientA);
        await CreateBillAsync(clientA, categoriesA[0].Id, "Aluguel", 1000m);
        await CreateRecurringIncomeAsync(clientA);
        await PostProjectionAsync(clientA, year);

        // Act — user B queries the same year/month (B has no entries)
        var (resp, body) = await GetEntriesAsync(clientB, year, 1);

        // Assert — B sees empty lists and zero totals; A's entries are invisible
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Bills, Is.Empty);
            Assert.That(body.Incomes, Is.Empty);
            Assert.That(body.Totals.BillsPlanned, Is.Zero);
            Assert.That(body.Totals.IncomesPlanned, Is.Zero);
        }
    }

    // --- Auth / validation ---

    [Test]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        using var response = await Client.GetAsync($"{EntriesUri}?year=2025&month=1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(0)]
    [TestCase(13)]
    public async Task Get_InvalidMonth_ReturnsValidationProblem(int invalidMonth)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync($"{EntriesUri}?year=2025&month={invalidMonth}");

        // Assert
        await AssertValidationProblemAsync(response, "month");
    }

    [TestCase(1999)]
    [TestCase(2101)]
    public async Task Get_YearOutOfRange_ReturnsValidationProblem(int invalidYear)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync($"{EntriesUri}?year={invalidYear}&month=1");

        // Assert
        await AssertValidationProblemAsync(response, "year");
    }

    [Test]
    public async Task Get_MissingYearAndMonth_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync(EntriesUri);

        // Assert
        await AssertValidationProblemAsync(response, "year", "month");
    }

    // --- Helpers ---

    // Fetches default categories; triggers user provisioning on first call.
    private static async Task<CategoryDto[]> GetDefaultCategoriesAsync(HttpClient client)
    {
        using var resp = await client.GetAsync("/api/v1/categories");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Has.Length.GreaterThanOrEqualTo(2), "Expected seeded default categories.");
        return dtos!;
    }

    private static async Task<long> CreatePersonAsync(HttpClient client, string name = "Parceiro")
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/persons", new { name });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    // Creates a recurring bill with the given split and returns the DTO.
    private static async Task<BillDto> CreateBillAsync(
        HttpClient client, long categoryId, string name, decimal amount, decimal splitRatio = 1m, long? personId = null)
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/bills",
            new { name, categoryId, kind = "recurring", defaultAmount = amount, splitRatio, personId });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    private static async Task<IncomeDto> CreateRecurringIncomeAsync(HttpClient client, string name = "Salario", decimal amount = 5000m)
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/incomes", new { name, kind = "recurring", defaultAmount = amount });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    private static async Task PostProjectionAsync(HttpClient client, int year)
    {
        using var resp = await client.PostAsync($"/api/v1/projection/{year}", null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task DeleteAsync(HttpClient client, string uri)
    {
        using var resp = await client.DeleteAsync(uri);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    private static async Task<(HttpResponseMessage Response, MonthEntriesResponse? Body)> GetEntriesAsync(
        HttpClient client, int year, int month)
    {
        var resp = await client.GetAsync($"{EntriesUri}?year={year}&month={month}");
        if (!resp.IsSuccessStatusCode)
            return (resp, null);
        return (resp, await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>());
    }

    // Marks a bill entry's split as received via POST /api/v1/receivables/{entryId}/mark.
    private static async Task MarkReceivedAsync(HttpClient client, long entryId)
    {
        using var resp = await client.PostAsJsonAsync($"/api/v1/receivables/{entryId}/mark", new { receivedDate = (DateOnly?)null });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task PayBillEntryAsync(HttpClient client, long entryId, decimal actualAmount)
    {
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/bill/{entryId}/pay", new { actualAmount });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task ReceiveIncomeEntryAsync(HttpClient client, long entryId, decimal actualAmount)
    {
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/income/{entryId}/receive", new { actualAmount });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // --- Local DTOs for JSON deserialization ---

    private sealed record MonthEntriesResponse(
        int Year, int Month,
        BillEntryResponse[] Bills,
        IncomeEntryResponse[] Incomes,
        MonthTotalsResponse Totals);

    private sealed record BillEntryResponse(
        long Id, long BillId, string Name, string Category,
        decimal PlannedAmount, decimal? ActualAmount, decimal SplitRatio, string? Person,
        decimal EffectiveAmount, decimal MyShare, decimal Receivable,
        bool Paid, DateTimeOffset? PaidDate, bool Received, DateTimeOffset? ReceivedDate);

    private sealed record IncomeEntryResponse(
        long Id, long IncomeId, string Name,
        decimal PlannedAmount, decimal? ActualAmount,
        decimal EffectiveAmount, bool Received, DateTimeOffset? ReceivedDate);

    private sealed record MonthTotalsResponse(
        decimal BillsPlanned, decimal BillsEffective,
        decimal MyShare, decimal Receivable, decimal Received,
        decimal ReceivablePending, decimal ReceivableReceived, decimal PaidFull,
        decimal IncomesPlanned, decimal IncomesEffective, decimal IncomesReceived,
        decimal SaldoPrevisto, decimal SaldoReal,
        decimal SaldoPrevistoOtimista, decimal SaldoPrevistoPiorCaso, decimal SaldoRealizado);

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
}
