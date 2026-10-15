using System.Net;
using System.Net.Http.Json;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for <c>POST /api/v1/bills/{billId}/recalculate</c>. Covers the happy path,
/// paid-entry freezing, the from-month boundary (including across years), the template's
/// DefaultAmount update, owner isolation, auth, and input validation.
/// </summary>
[TestFixture]
public sealed class RecalculateEndpointTests : IntegrationTestBase
{
    private const string BillsUri = "/api/v1/bills";

    // --- Happy path ---

    [Test]
    public async Task Recalculate_FromJuly_UpdatesJulyToDecemberUnpaid_LeavesJanuaryToJuneUntouched()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(client, defaultAmount: 100m);
        await PostProjectionAsync(client, 2026);

        // Act
        using var response = await RecalculateAsync(client, billId, 2026, 7, 175m);

        // Assert — response
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<RecalculateResponse>();
        Assert.That(body, Is.EqualTo(new RecalculateResponse(billId, 6, 0, 175m)));

        for (var m = 1; m <= 6; m++)
            Assert.That((await GetBillEntryAsync(client, 2026, m)).PlannedAmount, Is.EqualTo(100m), $"Month {m} should be untouched");
        for (var m = 7; m <= 12; m++)
            Assert.That((await GetBillEntryAsync(client, 2026, m)).PlannedAmount, Is.EqualTo(175m), $"Month {m} should be updated");
    }

    [Test]
    public async Task Recalculate_PaidMonthInRange_IsSkippedAndStaysFrozen()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(client, defaultAmount: 100m);
        await PostProjectionAsync(client, 2026);
        var august = await GetBillEntryAsync(client, 2026, 8);
        using (var payResp = await client.PostAsJsonAsync($"/api/v1/entries/bill/{august.Id}/pay", new { actualAmount = 110m }))
            Assert.That(payResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var response = await RecalculateAsync(client, billId, 2026, 7, 200m);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<RecalculateResponse>();
        Assert.That(body, Is.EqualTo(new RecalculateResponse(billId, 5, 1, 200m))); // Jul + Sep-Dec

        var paidAfter = await GetBillEntryAsync(client, 2026, 8);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(paidAfter.PlannedAmount, Is.EqualTo(100m));
            Assert.That(paidAfter.ActualAmount, Is.EqualTo(110m));
            Assert.That(paidAfter.Paid, Is.True);
            Assert.That((await GetBillEntryAsync(client, 2026, 7)).PlannedAmount, Is.EqualTo(200m));
            Assert.That((await GetBillEntryAsync(client, 2026, 9)).PlannedAmount, Is.EqualTo(200m));
        }
    }

    [Test]
    public async Task Recalculate_FromDecember_AlsoUpdatesFollowingYear()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(client, defaultAmount: 100m);
        await PostProjectionAsync(client, 2026);
        await PostProjectionAsync(client, 2027);

        // Act
        using var response = await RecalculateAsync(client, billId, 2026, 12, 50m);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<RecalculateResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Is.EqualTo(new RecalculateResponse(billId, 13, 0, 50m))); // Dec/2026 + 12 of 2027
            Assert.That((await GetBillEntryAsync(client, 2026, 11)).PlannedAmount, Is.EqualTo(100m));
            Assert.That((await GetBillEntryAsync(client, 2026, 12)).PlannedAmount, Is.EqualTo(50m));
            Assert.That((await GetBillEntryAsync(client, 2027, 6)).PlannedAmount, Is.EqualTo(50m));
        }
    }

    [Test]
    public async Task Recalculate_WithoutEntries_UpdatesBillDefaultAmount()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(client, defaultAmount: 100m);

        // Act
        using var response = await RecalculateAsync(client, billId, 2026, 1, 150m);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<RecalculateResponse>();
        Assert.That(body, Is.EqualTo(new RecalculateResponse(billId, 0, 0, 150m)));

        using var billsResp = await client.GetAsync(BillsUri);
        var bills = await billsResp.Content.ReadFromJsonAsync<BillSummaryDto[]>();
        Assert.That(bills![0].DefaultAmount, Is.EqualTo(150m));
    }

    // --- Not found / owner isolation ---

    [Test]
    public async Task Recalculate_OtherOwnerBill_ReturnsNotFoundAndLeavesEntriesUntouched()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(clientA, defaultAmount: 100m);
        await PostProjectionAsync(clientA, 2026);

        // Act
        using var response = await RecalculateAsync(clientB, billId, 2026, 7, 175m);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        Assert.That((await GetBillEntryAsync(clientA, 2026, 7)).PlannedAmount, Is.EqualTo(100m));
    }

    [Test]
    public async Task Recalculate_BillNotFound_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await RecalculateAsync(client, 999999, 2026, 7, 100m);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Recalculate_DeactivatedBill_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(client);
        using (var deleteResp = await client.DeleteAsync($"{BillsUri}/{billId}"))
            Assert.That(deleteResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        using var response = await RecalculateAsync(client, billId, 2026, 7, 100m);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // --- Auth ---

    [Test]
    public async Task Recalculate_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        using var response = await Client.PostAsJsonAsync($"{BillsUri}/1/recalculate",
            new { fromYear = 2026, fromMonth = 7, newAmount = 100m });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Validation ---

    [TestCase(0)]
    [TestCase(13)]
    public async Task Recalculate_InvalidMonth_ReturnsValidationProblem(int month)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(client);

        // Act
        using var response = await RecalculateAsync(client, billId, 2026, month, 100m);

        // Assert
        await AssertValidationProblemAsync(response, "fromMonth");
    }

    [Test]
    public async Task Recalculate_NegativeAmount_ReturnsValidationProblemAndKeepsEntries()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var billId = await CreateRecurringBillAsync(client, defaultAmount: 100m);
        await PostProjectionAsync(client, 2026);

        // Act
        using var response = await RecalculateAsync(client, billId, 2026, 7, -1m);

        // Assert
        await AssertValidationProblemAsync(response, "newAmount");
        Assert.That((await GetBillEntryAsync(client, 2026, 7)).PlannedAmount, Is.EqualTo(100m));
    }

    [Test]
    public async Task Recalculate_AllFieldsInvalid_ReturnsOneErrorPerField()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await RecalculateAsync(client, 999999, 2026, 0, -1m);

        // Assert
        await AssertValidationProblemAsync(response, "fromMonth", "newAmount");
    }

    // --- Helpers ---

    private static Task<HttpResponseMessage> RecalculateAsync(HttpClient client, long billId, int fromYear, int fromMonth, decimal newAmount) =>
        client.PostAsJsonAsync($"{BillsUri}/{billId}/recalculate", new { fromYear, fromMonth, newAmount });

    private static async Task<long> CreateRecurringBillAsync(HttpClient client, decimal defaultAmount = 100m)
    {
        using var categoriesResp = await client.GetAsync("/api/v1/categories");
        var categories = await categoriesResp.Content.ReadFromJsonAsync<CategoryDto[]>();

        using var response = await client.PostAsJsonAsync(BillsUri,
            new { name = "Energia", categoryId = categories![0].Id, kind = "recurring", defaultAmount, splitRatio = 1m, personId = (long?)null });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<BillSummaryDto>())!.Id;
    }

    private static async Task PostProjectionAsync(HttpClient client, int year)
    {
        using var response = await client.PostAsync($"/api/v1/projection/{year}", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<EntryDto> GetBillEntryAsync(HttpClient client, int year, int month)
    {
        using var response = await client.GetAsync($"/api/v1/entries?year={year}&month={month}");
        var body = await response.Content.ReadFromJsonAsync<MonthEntriesResponse>();
        return body!.Bills.Single();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem!.Status, Is.EqualTo((int)expectedStatus));
        }
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, params string[] expectedFields)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem!.Status, Is.EqualTo(400));
            Assert.That(problem.Errors.Keys, Is.EquivalentTo(expectedFields));
        }
    }

    private sealed record RecalculateResponse(long BillId, int UpdatedEntries, int SkippedPaid, decimal NewDefaultAmount);

    private sealed record MonthEntriesResponse(int Year, int Month, EntryDto[] Bills, object[] Incomes);

    private sealed record EntryDto(long Id, decimal PlannedAmount, decimal? ActualAmount, bool Paid);

    private sealed record BillSummaryDto(long Id, string Name, decimal DefaultAmount);

    private sealed record CategoryDto(long Id, string Name);

    private sealed record ProblemBody(int Status, string? Title, string? Detail);

    private sealed record ValidationProblemBody(int Status, Dictionary<string, string[]> Errors);
}
