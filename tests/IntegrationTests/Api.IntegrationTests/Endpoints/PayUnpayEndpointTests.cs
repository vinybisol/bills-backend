using System.Net;
using System.Net.Http.Json;
using static Api.IntegrationTests.TestSupport.ProblemAssertions;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for PATCH/pay/unpay of bill entries and PATCH/receive/unreceive of income entries.
/// Covers immutability (frozen entries reject edits and re-pay/re-receive), unfreeze, independence of
/// paid and received, validation, owner isolation and authentication. Each test authenticates as a
/// fresh Firebase uid.
/// </summary>
[TestFixture]
public sealed class PayUnpayEndpointTests : IntegrationTestBase
{
    private const string EntriesUri = "/api/v1/entries";

    [TestCase("/bill/1", "PATCH")]
    [TestCase("/bill/1/pay", "POST")]
    [TestCase("/bill/1/unpay", "POST")]
    [TestCase("/income/1", "PATCH")]
    [TestCase("/income/1/receive", "POST")]
    [TestCase("/income/1/unreceive", "POST")]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized(string uri, string method)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{EntriesUri}{uri}")
        {
            Content = JsonContent.Create(new { plannedAmount = 100m })
        };

        // Act
        using var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- PATCH /entries/bill/{id} ---

    [Test]
    public async Task PatchBillEntry_UnpaidEntry_UpdatesAmounts()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 1);

        // Act
        using var resp = await PatchAsync(client, $"{EntriesUri}/bill/{entryId}", new { plannedAmount = 1100m, actualAmount = 1090m });

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Id, Is.EqualTo(entryId));
            Assert.That(body.PlannedAmount, Is.EqualTo(1100m));
            Assert.That(body.ActualAmount, Is.EqualTo(1090m));
            Assert.That(body.Paid, Is.False);
        }
    }

    [Test]
    public async Task PatchBillEntry_PaidEntry_Returns409AndKeepsAmounts()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 2);
        await PayAsync(client, entryId, new { actualAmount = 990m });

        // Act
        using var patchResp = await PatchAsync(client, $"{EntriesUri}/bill/{entryId}", new { plannedAmount = 1200m });

        // Assert
        await AssertProblemAsync(patchResp, HttpStatusCode.Conflict);
        var entry = await GetBillEntryAsync(client, 2);
        Assert.That((entry.PlannedAmount, entry.ActualAmount, entry.Paid), Is.EqualTo((1000m, (decimal?)990m, true)));
    }

    [TestCase(-1, null, "plannedAmount")]
    [TestCase(null, -1, "actualAmount")]
    public async Task PatchBillEntry_NegativeAmount_ReturnsValidationProblem(double? planned, double? actual, string field)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 3);

        // Act
        using var resp = await PatchAsync(client, $"{EntriesUri}/bill/{entryId}",
            new { plannedAmount = (decimal?)planned, actualAmount = (decimal?)actual });

        // Assert
        await AssertValidationProblemAsync(resp, field);
    }

    [Test]
    public async Task PatchBillEntry_NonexistentEntry_Returns404()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var resp = await PatchAsync(client, $"{EntriesUri}/bill/999999999", new { plannedAmount = 1m });

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
    }

    // --- POST /entries/bill/{id}/pay ---

    [Test]
    public async Task PayBillEntry_SetsActualAmountToPlannedWhenOmitted()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 3);

        // Act
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/bill/{entryId}/pay", new { });

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Paid, Is.True);
            Assert.That(body.PaidDate, Is.Not.Null);
            Assert.That(body.ActualAmount, Is.EqualTo(1000m));
        }
    }

    [Test]
    public async Task PayBillEntry_WithoutBody_PaysPlannedAmount()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 3);

        // Act
        using var resp = await client.PostAsync($"{EntriesUri}/bill/{entryId}/pay", null);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That((body!.Paid, body.ActualAmount), Is.EqualTo((true, (decimal?)1000m)));
    }

    [Test]
    public async Task PayBillEntry_WithActualAmountAndDate_RecordsThem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 4);

        // Act
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/bill/{entryId}/pay",
            new { actualAmount = 980m, paidDate = "2025-04-10" });

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.ActualAmount, Is.EqualTo(980m));
            Assert.That(body.PaidDate, Is.EqualTo(new DateTimeOffset(2025, 4, 10, 0, 0, 0, TimeSpan.Zero)));
        }
    }

    [Test]
    public async Task PayBillEntry_AlreadyPaid_Returns409AndKeepsFrozenValues()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 5);
        await PayAsync(client, entryId, new { actualAmount = 950m, paidDate = "2025-05-05" });

        // Act
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/bill/{entryId}/pay", new { actualAmount = 1m });

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.Conflict);
        var entry = await GetBillEntryAsync(client, 5);
        Assert.That((entry.ActualAmount, entry.PaidDate), Is.EqualTo(((decimal?)950m, (DateTimeOffset?)new DateTimeOffset(2025, 5, 5, 0, 0, 0, TimeSpan.Zero))));
    }

    [Test]
    public async Task PayBillEntry_NegativeActualAmount_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 6);

        // Act
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/bill/{entryId}/pay", new { actualAmount = -5m });

        // Assert
        await AssertValidationProblemAsync(resp, "actualAmount");
        Assert.That((await GetBillEntryAsync(client, 6)).Paid, Is.False);
    }

    [Test]
    public async Task PayBillEntry_EntryBelongingToAnotherOwner_Returns404AndKeepsItUnpaid()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(clientA, 6);

        // Act — B tries to pay A's entry
        using var resp = await clientB.PostAsJsonAsync($"{EntriesUri}/bill/{entryId}/pay", new { });

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
        Assert.That((await GetBillEntryAsync(clientA, 6)).Paid, Is.False);
    }

    // --- POST /entries/bill/{id}/unpay ---

    [Test]
    public async Task UnpayBillEntry_FreezeThenUnfreeze_AllowsEdit()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 5);
        await PayAsync(client, entryId, new { });

        // Act
        using var unpayResp = await client.PostAsync($"{EntriesUri}/bill/{entryId}/unpay", null);

        // Assert
        Assert.That(unpayResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var unpayBody = await unpayResp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That((unpayBody!.Paid, unpayBody.PaidDate), Is.EqualTo((false, (DateTimeOffset?)null)));
        using var patchResp = await PatchAsync(client, $"{EntriesUri}/bill/{entryId}", new { plannedAmount = 1050m });
        Assert.That(patchResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task UnpayBillEntry_ReceivedEntry_KeepsReceivedIndependent()
    {
        // Arrange — split entry: I paid it AND the person paid me back
        using var client = CreateAuthenticatedClient();
        var personId = await CreatePersonAsync(client);
        var entryId = await ProjectBillEntryAsync(client, 7, splitRatio: 0.5m, personId: personId);
        await PayAsync(client, entryId, new { });
        using var mark = await client.PostAsJsonAsync($"/api/v1/receivables/{entryId}/mark", new { });
        Assert.That(mark.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        using var resp = await client.PostAsync($"{EntriesUri}/bill/{entryId}/unpay", null);

        // Assert — paid (I paid) is reverted; received (paid back to me) is untouched
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Paid, Is.False);
            Assert.That(body.Received, Is.True);
            Assert.That(body.ReceivedDate, Is.Not.Null);
        }
    }

    [Test]
    public async Task UnpayBillEntry_UnpaidEntry_IsIdempotent()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(client, 8);

        // Act
        using var resp = await client.PostAsync($"{EntriesUri}/bill/{entryId}/unpay", null);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await resp.Content.ReadFromJsonAsync<EntryResponse>())!.Paid, Is.False);
    }

    [Test]
    public async Task UnpayBillEntry_EntryBelongingToAnotherOwner_Returns404AndKeepsItPaid()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var entryId = await ProjectBillEntryAsync(clientA, 9);
        await PayAsync(clientA, entryId, new { });

        // Act
        using var resp = await clientB.PostAsync($"{EntriesUri}/bill/{entryId}/unpay", null);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
        Assert.That((await GetBillEntryAsync(clientA, 9)).Paid, Is.True);
    }

    // --- PATCH /entries/income/{id} ---

    [Test]
    public async Task PatchIncomeEntry_UnreceivedEntry_UpdatesAmounts()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 1);

        // Act
        using var resp = await PatchAsync(client, $"{EntriesUri}/income/{entryId}", new { plannedAmount = 5500m, actualAmount = 5300m });

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That((body!.PlannedAmount, body.ActualAmount), Is.EqualTo((5500m, (decimal?)5300m)));
    }

    [Test]
    public async Task PatchIncomeEntry_ReceivedEntry_Returns409()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 2);
        await ReceiveAsync(client, entryId, new { });

        // Act
        using var patchResp = await PatchAsync(client, $"{EntriesUri}/income/{entryId}", new { plannedAmount = 6000m });

        // Assert
        await AssertProblemAsync(patchResp, HttpStatusCode.Conflict);
        Assert.That((await GetIncomeEntryAsync(client, 2)).PlannedAmount, Is.EqualTo(5000m));
    }

    [Test]
    public async Task PatchIncomeEntry_NegativeAmount_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 2);

        // Act
        using var resp = await PatchAsync(client, $"{EntriesUri}/income/{entryId}", new { plannedAmount = -1m, actualAmount = -1m });

        // Assert
        await AssertValidationProblemAsync(resp, "plannedAmount", "actualAmount");
    }

    [Test]
    public async Task PatchIncomeEntry_EntryBelongingToAnotherOwner_Returns404()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(clientA, 2);

        // Act
        using var resp = await PatchAsync(clientB, $"{EntriesUri}/income/{entryId}", new { plannedAmount = 1m });

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
        Assert.That((await GetIncomeEntryAsync(clientA, 2)).PlannedAmount, Is.EqualTo(5000m));
    }

    // --- POST /entries/income/{id}/receive ---

    [Test]
    public async Task ReceiveIncomeEntry_SetsActualToPlannedWhenOmitted()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 3);

        // Act
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/income/{entryId}/receive", new { });

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body!.Received, Is.True);
            Assert.That(body.ReceivedDate, Is.Not.Null);
            Assert.That(body.ActualAmount, Is.EqualTo(5000m));
        }
    }

    [Test]
    public async Task ReceiveIncomeEntry_WithActualAmountAndDate_RecordsThem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 3);

        // Act
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/income/{entryId}/receive",
            new { actualAmount = 5100m, receivedDate = "2025-03-05" });

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That((body!.ActualAmount, body.ReceivedDate),
            Is.EqualTo(((decimal?)5100m, (DateTimeOffset?)new DateTimeOffset(2025, 3, 5, 0, 0, 0, TimeSpan.Zero))));
    }

    [Test]
    public async Task ReceiveIncomeEntry_AlreadyReceived_Returns409()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 4);
        await ReceiveAsync(client, entryId, new { actualAmount = 4800m });

        // Act
        using var resp = await client.PostAsync($"{EntriesUri}/income/{entryId}/receive", null);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.Conflict);
        Assert.That((await GetIncomeEntryAsync(client, 4)).ActualAmount, Is.EqualTo(4800m));
    }

    [Test]
    public async Task ReceiveIncomeEntry_NegativeActualAmount_ReturnsValidationProblem()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 4);

        // Act
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/income/{entryId}/receive", new { actualAmount = -1m });

        // Assert
        await AssertValidationProblemAsync(resp, "actualAmount");
    }

    [Test]
    public async Task ReceiveIncomeEntry_NonexistentEntry_Returns404()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var resp = await client.PostAsync($"{EntriesUri}/income/999999999/receive", null);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
    }

    // --- POST /entries/income/{id}/unreceive ---

    [Test]
    public async Task UnreceiveIncomeEntry_FreezeThenUnfreeze_AllowsEdit()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(client, 5);
        await ReceiveAsync(client, entryId, new { });

        // Act
        using var unrecvResp = await client.PostAsync($"{EntriesUri}/income/{entryId}/unreceive", null);

        // Assert
        Assert.That(unrecvResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await unrecvResp.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.That((body!.Received, body.ReceivedDate), Is.EqualTo((false, (DateTimeOffset?)null)));
        using var patchResp = await PatchAsync(client, $"{EntriesUri}/income/{entryId}", new { plannedAmount = 5100m });
        Assert.That(patchResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task UnreceiveIncomeEntry_EntryBelongingToAnotherOwner_Returns404AndKeepsItReceived()
    {
        // Arrange
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var entryId = await ProjectIncomeEntryAsync(clientA, 6);
        await ReceiveAsync(clientA, entryId, new { });

        // Act
        using var resp = await clientB.PostAsync($"{EntriesUri}/income/{entryId}/unreceive", null);

        // Assert
        await AssertProblemAsync(resp, HttpStatusCode.NotFound);
        Assert.That((await GetIncomeEntryAsync(clientA, 6)).Received, Is.True);
    }

    // --- Helpers ---

    private const int Year = 2025;

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string uri, object body) =>
        client.PatchAsJsonAsync(uri, body);

    private static async Task<long> CreatePersonAsync(HttpClient client)
    {
        using var resp = await client.PostAsJsonAsync("/api/v1/persons", new { name = "Parceiro" });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    // Creates a recurring bill (default 1000), projects the year and returns the entry of the given month.
    private static async Task<long> ProjectBillEntryAsync(HttpClient client, int month, decimal splitRatio = 1m, long? personId = null)
    {
        using var categories = await client.GetAsync("/api/v1/categories");
        var categoryId = (await categories.Content.ReadFromJsonAsync<IdDto[]>())![0].Id;
        using var bill = await client.PostAsJsonAsync("/api/v1/bills",
            new { name = "Aluguel", categoryId, kind = "recurring", defaultAmount = 1000m, splitRatio, personId });
        Assert.That(bill.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        await ProjectAsync(client);
        return (await GetBillEntryAsync(client, month)).Id;
    }

    // Creates a recurring income (default 5000), projects the year and returns the entry of the given month.
    private static async Task<long> ProjectIncomeEntryAsync(HttpClient client, int month)
    {
        using var income = await client.PostAsJsonAsync("/api/v1/incomes", new { name = "Salario", kind = "recurring", defaultAmount = 5000m });
        Assert.That(income.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        await ProjectAsync(client);
        return (await GetIncomeEntryAsync(client, month)).Id;
    }

    private static async Task ProjectAsync(HttpClient client)
    {
        using var resp = await client.PostAsync($"/api/v1/projection/{Year}", null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task PayAsync(HttpClient client, long entryId, object body)
    {
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/bill/{entryId}/pay", body);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task ReceiveAsync(HttpClient client, long entryId, object body)
    {
        using var resp = await client.PostAsJsonAsync($"{EntriesUri}/income/{entryId}/receive", body);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<MonthEntriesResponse> GetMonthAsync(HttpClient client, int month)
    {
        using var resp = await client.GetAsync($"{EntriesUri}?year={Year}&month={month}");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>())!;
    }

    private static async Task<EntryResponse> GetBillEntryAsync(HttpClient client, int month) =>
        (await GetMonthAsync(client, month)).Bills.Single();

    private static async Task<EntryResponse> GetIncomeEntryAsync(HttpClient client, int month) =>
        (await GetMonthAsync(client, month)).Incomes.Single();

    // --- Local DTOs ---

    private sealed record EntryResponse(
        long Id, decimal PlannedAmount, decimal? ActualAmount,
        bool Paid, DateTimeOffset? PaidDate,
        bool Received, DateTimeOffset? ReceivedDate);

    private sealed record MonthEntriesResponse(int Year, int Month, EntryResponse[] Bills, EntryResponse[] Incomes);

    private sealed record IdDto(long Id);
}
