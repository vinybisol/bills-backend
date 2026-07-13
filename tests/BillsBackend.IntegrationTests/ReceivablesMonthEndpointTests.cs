using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for the receivables month panel (<c>GET /api/receivables/month</c>) and the
/// mark/unmark/mark-batch endpoints, covering per-person grouping, split exclusion, idempotency,
/// the Paid/Received independence invariant, owner isolation, and authentication.
/// </summary>
[TestFixture]
public sealed class ReceivablesMonthEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-receivables-month-{suffix}";

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
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos![0].Id;
    }

    private async Task<long> CreatePersonAsync(string uid, string name)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/persons", uid, new { name });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    private async Task<long> CreateBillAsync(
        string uid, long categoryId, string name, decimal amount, decimal splitRatio, long? personId)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name, categoryId, kind = "one_off", defaultAmount = amount, splitRatio, personId });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!.Id;
    }

    private async Task<long> CreateBillEntryAsync(string uid, long billId, int year, int month, decimal plannedAmount)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/entries/bill", uid,
            new { billId, year, month, plannedAmount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillEntryResponse>())!.Id;
    }

    private async Task<(HttpStatusCode Status, ReceivablesMonthResponse? Body)> GetPanelAsync(string uid, int year, int month)
    {
        using var req = Req(HttpMethod.Get, $"/api/v1/receivables/month?year={year}&month={month}", uid);
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ReceivablesMonthResponse>() : null;
        return (resp.StatusCode, body);
    }

    private async Task<(HttpStatusCode Status, EntryResponse? Body)> MarkAsync(string uid, long entryId, DateOnly? receivedDate = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, $"/api/v1/receivables/{entryId}/mark", uid, new { receivedDate });
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<EntryResponse>() : null;
        return (resp.StatusCode, body);
    }

    private async Task<(HttpStatusCode Status, EntryResponse? Body)> UnmarkAsync(string uid, long entryId)
    {
        using var req = Req(HttpMethod.Post, $"/api/v1/receivables/{entryId}/unmark", uid);
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<EntryResponse>() : null;
        return (resp.StatusCode, body);
    }

    private async Task<(HttpStatusCode Status, MarkBatchResponse? Body)> MarkBatchAsync(
        string uid, IReadOnlyList<long> entryIds, DateOnly? receivedDate = null)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/receivables/mark-batch", uid, new { entryIds, receivedDate });
        using var resp = await Client.SendAsync(req);
        var body = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<MarkBatchResponse>() : null;
        return (resp.StatusCode, body);
    }

    // --- Panel: grouping and exclusion ---

    [Test]
    public async Task GetPanel_GroupsEntriesByPerson()
    {
        // Arrange
        var uid = Uid("group-by-person");
        var catId = await GetFirstCategoryIdAsync(uid);
        var esposa = await CreatePersonAsync(uid, "Esposa");
        var rentBill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, esposa);
        var phoneBill = await CreateBillAsync(uid, catId, "Telefone", 100m, 0.5m, esposa);
        await CreateBillEntryAsync(uid, rentBill, 2026, 7, 1000m);
        await CreateBillEntryAsync(uid, phoneBill, 2026, 7, 100m);

        // Act
        var (status, body) = await GetPanelAsync(uid, 2026, 7);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.ByPerson, Has.Length.EqualTo(1));
            Assert.That(body.ByPerson[0].PersonId, Is.EqualTo(esposa));
            Assert.That(body.ByPerson[0].Items, Has.Length.EqualTo(2));
            Assert.That(body.ByPerson[0].TotalDevido, Is.EqualTo(550m)); // 500 + 50
            Assert.That(body.TotalPendenteGeral, Is.EqualTo(550m));
        });
    }

    [Test]
    public async Task GetPanel_ExcludesEntriesWithFullSplit()
    {
        // Arrange
        var uid = Uid("exclude-full-split");
        var catId = await GetFirstCategoryIdAsync(uid);
        var esposa = await CreatePersonAsync(uid, "Esposa");
        var sharedBill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, esposa);
        var fullyMineBill = await CreateBillAsync(uid, catId, "Netflix", 50m, 1m, null);
        await CreateBillEntryAsync(uid, sharedBill, 2026, 8, 1000m);
        await CreateBillEntryAsync(uid, fullyMineBill, 2026, 8, 50m);

        // Act
        var (status, body) = await GetPanelAsync(uid, 2026, 8);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.ByPerson, Has.Length.EqualTo(1));
            Assert.That(body.ByPerson[0].Items, Has.Length.EqualTo(1));
            Assert.That(body.ByPerson[0].Items[0].Receivable, Is.EqualTo(500m));
        });
    }

    [Test]
    public async Task GetPanel_OwnerIsolation_OnlyReturnsAuthenticatedOwnersEntries()
    {
        // Arrange
        var uidA = Uid("isolate-a");
        var uidB = Uid("isolate-b");
        var catIdA = await GetFirstCategoryIdAsync(uidA);
        var personA = await CreatePersonAsync(uidA, "Esposa");
        var billA = await CreateBillAsync(uidA, catIdA, "Aluguel", 800m, 0.5m, personA);
        await CreateBillEntryAsync(uidA, billA, 2026, 9, 800m);

        await GetFirstCategoryIdAsync(uidB); // provision B

        // Act
        var (statusA, bodyA) = await GetPanelAsync(uidA, 2026, 9);
        var (statusB, bodyB) = await GetPanelAsync(uidB, 2026, 9);

        // Assert
        Assert.That(statusA, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(statusB, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(bodyA!.ByPerson, Has.Length.EqualTo(1));
            Assert.That(bodyB!.ByPerson, Is.Empty);
        });
    }

    // --- Mark individual ---

    [Test]
    public async Task Mark_ValidEntry_SetsReceivedAndReceivedDate()
    {
        // Arrange
        var uid = Uid("mark-ok");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var entryId = await CreateBillEntryAsync(uid, bill, 2026, 7, 1000m);

        // Act
        var (status, body) = await MarkAsync(uid, entryId, new DateOnly(2026, 7, 10));

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Received, Is.True);
            Assert.That(body.ReceivedDate, Is.EqualTo(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task Mark_FullSplitEntry_ReturnsBadRequest()
    {
        // Arrange — split == 1.0 is not a receivable
        var uid = Uid("mark-full-split");
        var catId = await GetFirstCategoryIdAsync(uid);
        var bill = await CreateBillAsync(uid, catId, "Netflix", 50m, 1m, null);
        var entryId = await CreateBillEntryAsync(uid, bill, 2026, 7, 50m);

        // Act
        var (status, _) = await MarkAsync(uid, entryId);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Mark_EntryBelongingToAnotherOwner_ReturnsNotFound()
    {
        // Arrange
        var uidA = Uid("owner-a-mark");
        var uidB = Uid("owner-b-mark");
        var catId = await GetFirstCategoryIdAsync(uidA);
        var person = await CreatePersonAsync(uidA, "Esposa");
        var bill = await CreateBillAsync(uidA, catId, "Aluguel", 1000m, 0.5m, person);
        var entryId = await CreateBillEntryAsync(uidA, bill, 2026, 7, 1000m);

        await GetFirstCategoryIdAsync(uidB); // provision B

        // Act
        var (status, _) = await MarkAsync(uidB, entryId);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Mark_Idempotent_MarkingTwiceSucceeds()
    {
        // Arrange
        var uid = Uid("mark-idempotent");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var entryId = await CreateBillEntryAsync(uid, bill, 2026, 7, 1000m);
        await MarkAsync(uid, entryId);

        // Act
        var (status, body) = await MarkAsync(uid, entryId);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Received, Is.True);
    }

    // --- Unmark individual ---

    [Test]
    public async Task Unmark_ReversesMark()
    {
        // Arrange
        var uid = Uid("unmark-ok");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var entryId = await CreateBillEntryAsync(uid, bill, 2026, 7, 1000m);
        await MarkAsync(uid, entryId);

        // Act
        var (status, body) = await UnmarkAsync(uid, entryId);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Received, Is.False);
            Assert.That(body.ReceivedDate, Is.Null);
        });
    }

    // --- Paid/Received independence ---

    [Test]
    public async Task Mark_DoesNotChangePaidOrPaidDate()
    {
        // Arrange — the owner has already paid the bill
        var uid = Uid("mark-paid-independent");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var entryId = await CreateBillEntryAsync(uid, bill, 2026, 7, 1000m);
        using var payReq = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { });
        using var payResp = await Client.SendAsync(payReq);
        var paidBody = await payResp.Content.ReadFromJsonAsync<EntryResponse>();

        // Act
        var (status, body) = await MarkAsync(uid, entryId);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Paid, Is.True);
            // Postgres stores timestamptz at microsecond precision, while the /pay response reflects
            // the tick-precision in-memory value assigned before SaveChangesAsync; tolerate that gap.
            Assert.That(body.PaidDate!.Value, Is.EqualTo(paidBody!.PaidDate!.Value).Within(TimeSpan.FromMilliseconds(1)));
            Assert.That(body.Received, Is.True);
        });
    }

    [Test]
    public async Task Unmark_DoesNotChangePaidOrPaidDate()
    {
        // Arrange — the owner has paid, and the split has been received
        var uid = Uid("unmark-paid-independent");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var bill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var entryId = await CreateBillEntryAsync(uid, bill, 2026, 7, 1000m);
        using var payReq = ReqWithBody(HttpMethod.Post, $"/api/v1/entries/bill/{entryId}/pay", uid, new { });
        using var payResp = await Client.SendAsync(payReq);
        var paidBody = await payResp.Content.ReadFromJsonAsync<EntryResponse>();
        await MarkAsync(uid, entryId);

        // Act
        var (status, body) = await UnmarkAsync(uid, entryId);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body!.Paid, Is.True);
            Assert.That(body.PaidDate!.Value, Is.EqualTo(paidBody!.PaidDate!.Value).Within(TimeSpan.FromMilliseconds(1)));
            Assert.That(body.Received, Is.False);
        });
    }

    // --- Mark batch ---

    [Test]
    public async Task MarkBatch_MarksMultipleEntriesAndReturnsCount()
    {
        // Arrange
        var uid = Uid("batch-ok");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var billA = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var billB = await CreateBillAsync(uid, catId, "Telefone", 100m, 0.5m, person);
        var entryA = await CreateBillEntryAsync(uid, billA, 2026, 7, 1000m);
        var entryB = await CreateBillEntryAsync(uid, billB, 2026, 7, 100m);

        // Act
        var (status, body) = await MarkBatchAsync(uid, [entryA, entryB]);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Marked, Is.EqualTo(2));

        var (_, panel) = await GetPanelAsync(uid, 2026, 7);
        Assert.That(panel!.ByPerson[0].Items.All(i => i.Received), Is.True);
    }

    [Test]
    public async Task MarkBatch_AnyIdBelongingToAnotherOwner_RejectsAndMarksNothing()
    {
        // Arrange
        var uidA = Uid("batch-owner-a");
        var uidB = Uid("batch-owner-b");
        var catIdA = await GetFirstCategoryIdAsync(uidA);
        var personA = await CreatePersonAsync(uidA, "Esposa");
        var billA1 = await CreateBillAsync(uidA, catIdA, "Aluguel", 1000m, 0.5m, personA);
        var billA2 = await CreateBillAsync(uidA, catIdA, "Telefone", 100m, 0.5m, personA);
        var entryA1 = await CreateBillEntryAsync(uidA, billA1, 2026, 7, 1000m);
        var entryA2 = await CreateBillEntryAsync(uidA, billA2, 2026, 7, 100m);

        var catIdB = await GetFirstCategoryIdAsync(uidB);
        var personB = await CreatePersonAsync(uidB, "Marido");
        var billB = await CreateBillAsync(uidB, catIdB, "Agua", 200m, 0.5m, personB);
        var entryB = await CreateBillEntryAsync(uidB, billB, 2026, 7, 200m);

        // Act — A tries to batch-mark two of its own entries plus B's entry
        var (status, _) = await MarkBatchAsync(uidA, [entryA1, entryA2, entryB]);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        var (_, panel) = await GetPanelAsync(uidA, 2026, 7);
        Assert.That(panel!.ByPerson[0].Items.Any(i => i.Received), Is.False, "Nothing should have been marked.");
    }

    [Test]
    public async Task MarkBatch_AnyIdWithFullSplit_RejectsAndMarksNothing()
    {
        // Arrange
        var uid = Uid("batch-full-split");
        var catId = await GetFirstCategoryIdAsync(uid);
        var person = await CreatePersonAsync(uid, "Esposa");
        var sharedBill = await CreateBillAsync(uid, catId, "Aluguel", 1000m, 0.5m, person);
        var fullyMineBill = await CreateBillAsync(uid, catId, "Netflix", 50m, 1m, null);
        var sharedEntry = await CreateBillEntryAsync(uid, sharedBill, 2026, 7, 1000m);
        var fullyMineEntry = await CreateBillEntryAsync(uid, fullyMineBill, 2026, 7, 50m);

        // Act
        var (status, _) = await MarkBatchAsync(uid, [sharedEntry, fullyMineEntry]);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        var (_, panel) = await GetPanelAsync(uid, 2026, 7);
        Assert.That(panel!.ByPerson[0].Items.Any(i => i.Received), Is.False, "Nothing should have been marked.");
    }

    // --- Auth ---

    [Test]
    public async Task GetPanel_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/receivables/month?year=2026&month=7");

        // Act
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Mark_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/receivables/1/mark");
        req.Content = JsonContent.Create(new { });

        // Act
        using var resp = await Client.SendAsync(req);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Local helpers / DTOs for JSON deserialization ---

    private sealed record BillDto(long Id, string Name);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
    private sealed record BillEntryResponse(long Id, long BillId, int RefYear, int RefMonth);

    private sealed record EntryResponse(
        long Id, decimal PlannedAmount, decimal? ActualAmount,
        bool Paid, DateTimeOffset? PaidDate,
        bool Received, DateTimeOffset? ReceivedDate);

    private sealed record ReceivableItemResponse(long EntryId, string Bill, decimal Receivable, bool Received);

    private sealed record PersonReceivablesResponse(
        long PersonId, string Name, decimal TotalDevido, decimal JaRecebido, decimal Pendente,
        ReceivableItemResponse[] Items);

    private sealed record ReceivablesMonthResponse(
        int Year, int Month, PersonReceivablesResponse[] ByPerson, decimal TotalPendenteGeral);

    private sealed record MarkBatchResponse(int Marked);
}
