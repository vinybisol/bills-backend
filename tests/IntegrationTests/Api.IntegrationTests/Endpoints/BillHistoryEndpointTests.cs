using System.Net;
using System.Net.Http.Json;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for <c>GET /api/v1/bills/{billId}/history</c>, covering the header/summary/items
/// payload, period filtering, soft-deleted templates, owner scoping, and authentication.
/// </summary>
[TestFixture]
public sealed class BillHistoryEndpointTests : IntegrationTestBase
{
    // --- Header + summary + items ---

    [Test]
    public async Task Get_ReturnsHeaderSummaryAndItems()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var category = await GetFirstCategoryAsync(client);
        var esposa = await CreatePersonAsync(client, "Esposa");
        var bill = await CreateBillAsync(client, category.Id, "Rodotos", 150m, 0.5m, esposa);
        var jan = await CreateBillEntryAsync(client, bill, 2026, 1, 150m);
        await PayAsync(client, jan);
        await CreateBillEntryAsync(client, bill, 2026, 2, 150m);

        // Act
        var (status, body) = await GetHistoryAsync(client, bill);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.BillId, Is.EqualTo(bill));
            Assert.That(body.Name, Is.EqualTo("Rodotos"));
            Assert.That(body.Category, Is.EqualTo(category.Name));
            Assert.That(body.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(body.Person, Is.EqualTo("Esposa"));
            Assert.That(body.Items, Has.Length.EqualTo(2));
            Assert.That(body.Items[0].Year, Is.EqualTo(2026));
            Assert.That(body.Items[0].Month, Is.EqualTo(1));
            Assert.That(body.Items[0].Paid, Is.True);
            Assert.That(body.Items[0].Variation, Is.Null);
            Assert.That(body.Items[1].Month, Is.EqualTo(2));
            Assert.That(body.Items[1].Variation, Is.EqualTo(new BillHistoryVariationResponse(0m, 0m)));
            Assert.That(body.Summary, Is.EqualTo(new BillHistorySummaryResponse(150m, 150m, 150m, 75m))); // only jan paid
        }
    }

    [Test]
    public async Task Get_UnsharedBill_HasNullPerson()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var category = await GetFirstCategoryAsync(client);
        var bill = await CreateBillAsync(client, category.Id, "Internet", 100m, 1m, null);
        await CreateBillEntryAsync(client, bill, 2026, 1, 100m);

        // Act
        var (status, body) = await GetHistoryAsync(client, bill);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Person, Is.Null);
    }

    [Test]
    public async Task Get_BillWithoutEntries_ReturnsEmptyItemsAndZeroSummary()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var category = await GetFirstCategoryAsync(client);
        var bill = await CreateBillAsync(client, category.Id, "Nova", 100m, 1m, null);

        // Act
        var (status, body) = await GetHistoryAsync(client, bill);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Items, Is.Empty);
            Assert.That(body.Summary, Is.EqualTo(new BillHistorySummaryResponse(0m, 0m, 0m, 0m)));
        }
    }

    [Test]
    public async Task Get_DeactivatedBillAndPerson_StillResolvesHistory()
    {
        // Arrange — soft-deleted templates keep their history readable
        using var client = CreateAuthenticatedClient();
        var category = await GetFirstCategoryAsync(client);
        var person = await CreatePersonAsync(client, "Ex");
        var bill = await CreateBillAsync(client, category.Id, "Antiga", 80m, 0.5m, person);
        await CreateBillEntryAsync(client, bill, 2026, 3, 80m);
        using (var deleteBill = await client.DeleteAsync($"/api/v1/bills/{bill}"))
            Assert.That(deleteBill.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using (var deletePerson = await client.DeleteAsync($"/api/v1/persons/{person}"))
            Assert.That(deletePerson.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        var (status, body) = await GetHistoryAsync(client, bill);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Name, Is.EqualTo("Antiga"));
            Assert.That(body.Person, Is.EqualTo("Ex"));
            Assert.That(body.Items.Select(i => i.Month), Is.EqualTo(new[] { 3 }));
        }
    }

    // --- Period filtering ---

    [Test]
    public async Task Get_PeriodFilter_NarrowsToWindow()
    {
        // Arrange — entries in Jan, May, Sep; filter to Mar-Jun
        using var client = CreateAuthenticatedClient();
        var category = await GetFirstCategoryAsync(client);
        var bill = await CreateBillAsync(client, category.Id, "Rodotos", 150m, 1m, null);
        await CreateBillEntryAsync(client, bill, 2026, 1, 150m);
        await CreateBillEntryAsync(client, bill, 2026, 5, 150m);
        await CreateBillEntryAsync(client, bill, 2026, 9, 150m);

        // Act
        var (status, body) = await GetHistoryAsync(client, bill, "?fromYear=2026&fromMonth=3&toYear=2026&toMonth=6");

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Items.Select(i => i.Month), Is.EqualTo(new[] { 5 }));
    }

    // --- Owner scoping ---

    [Test]
    public async Task Get_UnknownBillId_ReturnsNotFoundProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.GetAsync("/api/v1/bills/999999999/history");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Get_BillBelongingToAnotherOwner_ReturnsNotFoundProblem()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var category = await GetFirstCategoryAsync(clientA);
        var billA = await CreateBillAsync(clientA, category.Id, "Rodotos", 150m, 1m, null);
        await CreateBillEntryAsync(clientA, billA, 2026, 1, 150m);

        // Act
        using var response = await clientB.GetAsync($"/api/v1/bills/{billA}/history");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // --- Auth ---

    [Test]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        using var response = await Client.GetAsync("/api/v1/bills/1/history");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Helpers ---

    private static async Task<CategoryDto> GetFirstCategoryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/categories");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await response.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos![0];
    }

    private static async Task<long> CreatePersonAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/persons", new { name });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<long> CreateBillAsync(
        HttpClient client, long categoryId, string name, decimal amount, decimal splitRatio, long? personId)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bills",
            new { name, categoryId, kind = "one_off", defaultAmount = amount, splitRatio, personId });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<long> CreateBillEntryAsync(HttpClient client, long billId, int year, int month, decimal plannedAmount)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/entries/bill", new { billId, year, month, plannedAmount });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task PayAsync(HttpClient client, long entryId)
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/entries/bill/{entryId}/pay", new { });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<(HttpStatusCode Status, BillHistoryResponse? Body)> GetHistoryAsync(
        HttpClient client, long billId, string query = "")
    {
        using var response = await client.GetAsync($"/api/v1/bills/{billId}/history{query}");
        var body = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<BillHistoryResponse>() : null;
        return (response.StatusCode, body);
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

    private sealed record IdDto(long Id);

    private sealed record CategoryDto(long Id, string Name);

    private sealed record ProblemBody(int Status, string? Title, string? Detail);

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
