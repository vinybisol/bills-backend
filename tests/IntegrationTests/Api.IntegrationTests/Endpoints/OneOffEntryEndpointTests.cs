using System.Net;
using System.Net.Http.Json;
using static Api.IntegrationTests.TestSupport.ProblemAssertions;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for <c>POST /api/v1/entries/bill|income</c> (one-off entries) and
/// <c>DELETE /api/v1/entries/bill|income/{id}</c>. Covers snapshots, UNIQUE enforcement, validation,
/// owner isolation, immutability (paid/received entries are frozen) and authentication.
/// Each test authenticates as a fresh Firebase uid.
/// </summary>
[TestFixture]
public sealed class OneOffEntryEndpointTests : IntegrationTestBase
{
    private const string EntriesUri = "/api/v1/entries";

    [TestCase("/bill", "POST")]
    [TestCase("/income", "POST")]
    [TestCase("/bill/1", "DELETE")]
    [TestCase("/income/1", "DELETE")]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized(string uri, string method)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{EntriesUri}{uri}");
        if (method == "POST")
            request.Content = JsonContent.Create(new { billId = 1, incomeId = 1, year = 2026, month = 1 });

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Bill entry creation ---

    [Test]
    public async Task PostBillEntry_OneOffBill_Returns201WithSnapshotsAndLocation()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var catId = await GetFirstCategoryIdAsync(client);
        var personId = await CreatePersonAsync(client, "Esposa");
        var bill = await CreateBillAsync(client, catId, "IPVA", 1200m, splitRatio: 0.5m, personId: personId);

        // Act
        var (resp, body) = await PostBillEntryAsync(client, bill.Id, 2026, 4, plannedAmount: 1100m);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Id, Is.GreaterThan(0));
            Assert.That(body.BillId, Is.EqualTo(bill.Id));
            Assert.That(body.PlannedAmount, Is.EqualTo(1100m));
            Assert.That(body.ActualAmount, Is.Null);
            Assert.That(body.SplitRatioSnapshot, Is.EqualTo(0.5m));
            Assert.That(body.PersonId, Is.EqualTo(personId));
            Assert.That(body.Paid, Is.False);
            Assert.That(body.Received, Is.False);
            Assert.That(body.RefYear, Is.EqualTo(2026));
            Assert.That(body.RefMonth, Is.EqualTo(4));
            Assert.That(resp.Headers.Location?.ToString(), Is.EqualTo($"{EntriesUri}/bill/{body.Id}"));
        }
    }

    [Test]
    public async Task PostBillEntry_TemplateEditedAfterwards_EntryKeepsSnapshot()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var catId = await GetFirstCategoryIdAsync(client);
        var personId = await CreatePersonAsync(client);
        var bill = await CreateBillAsync(client, catId, "IPVA", 1200m, splitRatio: 0.5m, personId: personId);
        var (_, created) = await PostBillEntryAsync(client, bill.Id, 2026, 4);

        // Act — edit the molde after the entry exists
        using var put = await client.PutAsJsonAsync($"/api/v1/bills/{bill.Id}",
            new { name = "IPVA", categoryId = catId, kind = "one_off", defaultAmount = 9999m, splitRatio = 1m, personId = (long?)null });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var entry = (await GetMonthAsync(client, 2026, 4)).Bills.Single();

        // Assert
        Assert.That((entry.Id, entry.PlannedAmount, entry.SplitRatio), Is.EqualTo((created!.Id, 1200m, 0.5m)));
    }

    [Test]
    public async Task PostBillEntry_UsesDefaultAmountWhenPlannedAmountOmitted()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var catId = await GetFirstCategoryIdAsync(client);
        var bill = await CreateBillAsync(client, catId, "Revisao", 600m);

        // Act
        var (resp, body) = await PostBillEntryAsync(client, bill.Id, 2026, 5, plannedAmount: null);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(body!.PlannedAmount, Is.EqualTo(600m));
    }

    [Test]
    public async Task PostBillEntry_DuplicateBillMonth_Returns409()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var catId = await GetFirstCategoryIdAsync(client);
        var bill = await CreateBillAsync(client, catId);
        var (firstResp, _) = await PostBillEntryAsync(client, bill.Id, 2026, 6, 1200m);
        Assert.That(firstResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Act
        var (dupResp, _) = await PostBillEntryAsync(client, bill.Id, 2026, 6, 1200m);

        // Assert
        await AssertProblemAsync(dupResp, HttpStatusCode.Conflict);
    }

    [Test]
    public async Task PostBillEntry_RecurringBill_ReturnsValidationProblem()
    {
        // Arrange — recurring bills get their entries from the projection only
        using var client = CreateAuthenticatedClient();
        var catId = await GetFirstCategoryIdAsync(client);
        var bill = await CreateBillAsync(client, catId, kind: "recurring");

        // Act
        var (resp, _) = await PostBillEntryAsync(client, bill.Id, 2026, 7, 1000m);

        // Assert
        await AssertValidationProblemAsync(resp, "billId");
    }

    [Test]
    public async Task PostBillEntry_NonexistentBill_Returns404()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        var (resp, _) = await PostBillEntryAsync(client, 999_999_999, 2026, 7);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PostBillEntry_DeactivatedBill_Returns404()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var catId = await GetFirstCategoryIdAsync(client);
        var bill = await CreateBillAsync(client, catId);
        using var del = await client.DeleteAsync($"/api/v1/bills/{bill.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        var (resp, _) = await PostBillEntryAsync(client, bill.Id, 2026, 7);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PostBillEntry_BillBelongingToAnotherOwner_Returns404()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var billA = await CreateBillAsync(clientA, await GetFirstCategoryIdAsync(clientA));

        // Act — owner B tries to create an entry for owner A's bill
        var (resp, _) = await PostBillEntryAsync(clientB, billA.Id, 2026, 8, 1200m);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
        Assert.That((await GetMonthAsync(clientA, 2026, 8)).Bills, Is.Empty);
    }

    [TestCase(2026, 0, 100, "month")]
    [TestCase(2026, 13, 100, "month")]
    [TestCase(1999, 1, 100, "year")]
    [TestCase(2026, 1, -1, "plannedAmount")]
    public async Task PostBillEntry_InvalidInput_ReturnsValidationProblem(int year, int month, decimal plannedAmount, string field)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var bill = await CreateBillAsync(client, await GetFirstCategoryIdAsync(client));

        // Act
        var (resp, _) = await PostBillEntryAsync(client, bill.Id, year, month, plannedAmount);

        // Assert
        await AssertValidationProblemAsync(resp, field);
    }

    // --- Bill entry deletion ---

    [Test]
    public async Task DeleteBillEntry_UnpaidEntry_Returns204AndRemovesIt()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var bill = await CreateBillAsync(client, await GetFirstCategoryIdAsync(client));
        var (_, body) = await PostBillEntryAsync(client, bill.Id, 2026, 9, 1200m);

        // Act
        using var delResp = await client.DeleteAsync($"{EntriesUri}/bill/{body!.Id}");

        // Assert
        Assert.That(delResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await GetMonthAsync(client, 2026, 9)).Bills, Is.Empty);
    }

    [Test]
    public async Task DeleteBillEntry_PaidEntry_Returns409AndKeepsIt()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var bill = await CreateBillAsync(client, await GetFirstCategoryIdAsync(client));
        var (_, body) = await PostBillEntryAsync(client, bill.Id, 2026, 10, 1200m);
        using var pay = await client.PostAsync($"{EntriesUri}/bill/{body!.Id}/pay", null);
        Assert.That(pay.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var delResp = await client.DeleteAsync($"{EntriesUri}/bill/{body.Id}");

        // Assert
        await AssertProblemAsync(delResp, HttpStatusCode.Conflict);
        Assert.That((await GetMonthAsync(client, 2026, 10)).Bills.Select(b => b.Id), Is.EqualTo(new[] { body.Id }));
    }

    [Test]
    public async Task DeleteBillEntry_NonexistentEntry_Returns404()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var resp = await client.DeleteAsync($"{EntriesUri}/bill/999999999");

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteBillEntry_EntryBelongingToAnotherOwner_Returns404AndKeepsIt()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var bill = await CreateBillAsync(clientA, await GetFirstCategoryIdAsync(clientA));
        var (_, body) = await PostBillEntryAsync(clientA, bill.Id, 2026, 11);

        // Act
        using var resp = await clientB.DeleteAsync($"{EntriesUri}/bill/{body!.Id}");

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
        Assert.That((await GetMonthAsync(clientA, 2026, 11)).Bills, Has.Length.EqualTo(1));
    }

    // --- Income entry creation ---

    [Test]
    public async Task PostIncomeEntry_OneOffIncome_Returns201WithSnapshotAndLocation()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(client, "Reembolso", 500m);

        // Act
        var (resp, body) = await PostIncomeEntryAsync(client, income.Id, 2026, 4, plannedAmount: 450m);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.IncomeId, Is.EqualTo(income.Id));
            Assert.That(body.PlannedAmount, Is.EqualTo(450m));
            Assert.That(body.ActualAmount, Is.Null);
            Assert.That(body.Received, Is.False);
            Assert.That(body.RefYear, Is.EqualTo(2026));
            Assert.That(body.RefMonth, Is.EqualTo(4));
            Assert.That(resp.Headers.Location?.ToString(), Is.EqualTo($"{EntriesUri}/income/{body.Id}"));
        }
    }

    [Test]
    public async Task PostIncomeEntry_UsesDefaultAmountWhenPlannedAmountOmitted()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(client, amount: 320m);

        // Act
        var (resp, body) = await PostIncomeEntryAsync(client, income.Id, 2026, 5);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(body!.PlannedAmount, Is.EqualTo(320m));
    }

    [Test]
    public async Task PostIncomeEntry_DuplicateIncomeMonth_Returns409()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(client);
        var (firstResp, _) = await PostIncomeEntryAsync(client, income.Id, 2026, 6, 500m);
        Assert.That(firstResp.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Act
        var (dupResp, _) = await PostIncomeEntryAsync(client, income.Id, 2026, 6, 500m);

        // Assert
        await AssertProblemAsync(dupResp, HttpStatusCode.Conflict);
    }

    [Test]
    public async Task PostIncomeEntry_RecurringIncome_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(client, kind: "recurring");

        // Act
        var (resp, _) = await PostIncomeEntryAsync(client, income.Id, 2026, 7);

        // Assert
        await AssertValidationProblemAsync(resp, "incomeId");
    }

    [Test]
    public async Task PostIncomeEntry_IncomeBelongingToAnotherOwner_Returns404()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var incomeA = await CreateIncomeAsync(clientA);

        // Act
        var (resp, _) = await PostIncomeEntryAsync(clientB, incomeA.Id, 2026, 8, 500m);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
    }

    [TestCase(2026, 0, 100, "month")]
    [TestCase(2101, 1, 100, "year")]
    [TestCase(2026, 1, -0.01, "plannedAmount")]
    public async Task PostIncomeEntry_InvalidInput_ReturnsValidationProblem(int year, int month, decimal plannedAmount, string field)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(client);

        // Act
        var (resp, _) = await PostIncomeEntryAsync(client, income.Id, year, month, plannedAmount);

        // Assert
        await AssertValidationProblemAsync(resp, field);
    }

    // --- Income entry deletion ---

    [Test]
    public async Task DeleteIncomeEntry_UnreceivedEntry_Returns204AndRemovesIt()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(client);
        var (_, body) = await PostIncomeEntryAsync(client, income.Id, 2026, 9, 500m);

        // Act
        using var delResp = await client.DeleteAsync($"{EntriesUri}/income/{body!.Id}");

        // Assert
        Assert.That(delResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await GetMonthAsync(client, 2026, 9)).Incomes, Is.Empty);
    }

    [Test]
    public async Task DeleteIncomeEntry_ReceivedEntry_Returns409AndKeepsIt()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(client);
        var (_, body) = await PostIncomeEntryAsync(client, income.Id, 2026, 10, 500m);
        using var receive = await client.PostAsync($"{EntriesUri}/income/{body!.Id}/receive", null);
        Assert.That(receive.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var delResp = await client.DeleteAsync($"{EntriesUri}/income/{body.Id}");

        // Assert
        await AssertProblemAsync(delResp, HttpStatusCode.Conflict);
        Assert.That((await GetMonthAsync(client, 2026, 10)).Incomes, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task DeleteIncomeEntry_EntryBelongingToAnotherOwner_Returns404()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var income = await CreateIncomeAsync(clientA);
        var (_, body) = await PostIncomeEntryAsync(clientA, income.Id, 2026, 11);

        // Act
        using var resp = await clientB.DeleteAsync($"{EntriesUri}/income/{body!.Id}");

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
        Assert.That((await GetMonthAsync(clientA, 2026, 11)).Incomes, Has.Length.EqualTo(1));
    }

    // --- Helpers ---

    private static async Task<long> GetFirstCategoryIdAsync(HttpClient client)
    {
        using var resp = await client.GetAsync("/api/v1/categories");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos![0].Id;
    }

    private static async Task<long> CreatePersonAsync(HttpClient client, string name = "Parceiro")
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/persons", new { name });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    private static async Task<BillDto> CreateBillAsync(HttpClient client, long categoryId,
        string name = "IPVA", decimal amount = 1200m, decimal splitRatio = 1m, long? personId = null, string kind = "one_off")
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/bills",
            new { name, categoryId, kind, defaultAmount = amount, splitRatio, personId });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    private static async Task<IncomeDto> CreateIncomeAsync(HttpClient client,
        string name = "Reembolso", decimal amount = 500m, string kind = "one_off")
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/incomes", new { name, kind, defaultAmount = amount });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    private static async Task<(HttpResponseMessage Response, BillEntryResponse? Body)> PostBillEntryAsync(
        HttpClient client, long billId, int year, int month, decimal? plannedAmount = null)
    {
        var resp = await client.PostAsJsonAsync($"{EntriesUri}/bill", new { billId, year, month, plannedAmount });
        if (!resp.IsSuccessStatusCode)
            return (resp, null);
        return (resp, await resp.Content.ReadFromJsonAsync<BillEntryResponse>());
    }

    private static async Task<(HttpResponseMessage Response, IncomeEntryResponse? Body)> PostIncomeEntryAsync(
        HttpClient client, long incomeId, int year, int month, decimal? plannedAmount = null)
    {
        var resp = await client.PostAsJsonAsync($"{EntriesUri}/income", new { incomeId, year, month, plannedAmount });
        if (!resp.IsSuccessStatusCode)
            return (resp, null);
        return (resp, await resp.Content.ReadFromJsonAsync<IncomeEntryResponse>());
    }

    private static async Task<MonthEntriesResponse> GetMonthAsync(HttpClient client, int year, int month)
    {
        using var resp = await client.GetAsync($"{EntriesUri}?year={year}&month={month}");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>())!;
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

    private sealed record MonthEntriesResponse(int Year, int Month, MonthBill[] Bills, MonthIncome[] Incomes);
    private sealed record MonthBill(long Id, decimal PlannedAmount, decimal SplitRatio);
    private sealed record MonthIncome(long Id, decimal PlannedAmount);

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
}
