using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillsBackend.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/entries</c>, covering entry enrichment with names,
/// derived value calculations, owner isolation, authentication, and month validation.
/// </summary>
[TestFixture]
public sealed class EntriesEndpointTests
{
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private Respawner _respawner = null!;
    private NpgsqlConnection _dbConnection = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        _dbConnection = new NpgsqlConnection(_factory.TestConnectionString);
        await _dbConnection.OpenAsync();
        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }

    [SetUp]
    public async Task ResetDatabase() => await _respawner.ResetAsync(_dbConnection);

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _dbConnection.DisposeAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static string Uid(string suffix) => $"firebase-entries-{suffix}";

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

    // Fetches default categories for the given uid; triggers user provisioning on first call.
    private async Task<CategoryDto[]> GetDefaultCategoriesAsync(string uid)
    {
        using var req = Req(HttpMethod.Get, "/categories", uid);
        using var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos!;
    }

    // Creates a person and returns their id.
    private async Task<long> CreatePersonAsync(string uid, string name = "Parceiro")
    {
        using var req = ReqWithBody(HttpMethod.Post, "/persons", uid, new { name });
        using var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    // Creates a recurring bill without a split and returns the DTO.
    private async Task<BillDto> CreateRecurringBillAsync(
        string uid, long categoryId, string name = "Aluguel", decimal amount = 1000m)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/bills", uid,
            new { name, categoryId, kind = "recurring", defaultAmount = amount, splitRatio = 1m, personId = (long?)null });
        using var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    // Creates a recurring bill with a 50/50 split and returns the DTO.
    private async Task<BillDto> CreateSplitBillAsync(
        string uid, long categoryId, long personId,
        string name = "Internet", decimal amount = 120m)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/bills", uid,
            new { name, categoryId, kind = "recurring", defaultAmount = amount, splitRatio = 0.5m, personId = (long?)personId });
        using var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    // Creates a recurring income and returns the DTO.
    private async Task<IncomeDto> CreateRecurringIncomeAsync(string uid, string name = "Salario", decimal amount = 5000m)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/incomes", uid,
            new { name, kind = "recurring", defaultAmount = amount });
        using var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    // Runs POST /api/projection/{year} and asserts success.
    private async Task PostProjectionAsync(string uid, int year)
    {
        using var req = Req(HttpMethod.Post, $"/api/projection/{year}", uid);
        using var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // Calls GET /api/entries?year={year}&month={month} with the given uid.
    private async Task<(HttpResponseMessage Response, MonthEntriesResponse? Body)> GetEntriesAsync(
        string uid, int year, int month)
    {
        using var req = Req(HttpMethod.Get, $"/api/entries?year={year}&month={month}", uid);
        var resp = await _client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return (resp, null);
        var body = await resp.Content.ReadFromJsonAsync<MonthEntriesResponse>();
        return (resp, body);
    }

    // --- Tests ---

    [Test]
    public async Task Get_ValidYearMonth_ReturnsBillsAndIncomes()
    {
        // Arrange
        const int year = 2025;
        const int month = 1;
        var uid = Uid("valid");
        var categories = await GetDefaultCategoriesAsync(uid);
        await CreateRecurringBillAsync(uid, categories[0].Id, name: "Aluguel", amount: 1500m);
        await CreateRecurringIncomeAsync(uid, name: "Salario", amount: 5000m);
        await PostProjectionAsync(uid, year);

        // Act
        var (resp, body) = await GetEntriesAsync(uid, year, month);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Year, Is.EqualTo(year));
            Assert.That(body.Month, Is.EqualTo(month));
            Assert.That(body.Bills, Has.Length.EqualTo(1));
            Assert.That(body.Incomes, Has.Length.EqualTo(1));
        });

        var bill = body!.Bills[0];
        Assert.Multiple(() =>
        {
            Assert.That(bill.Name, Is.EqualTo("Aluguel"));
            Assert.That(bill.PlannedAmount, Is.EqualTo(1500m));
            Assert.That(bill.ActualAmount, Is.Null);
            Assert.That(bill.SplitRatio, Is.EqualTo(1m));
            Assert.That(bill.EffectiveAmount, Is.EqualTo(1500m));
            Assert.That(bill.MyShare, Is.EqualTo(1500m));
            Assert.That(bill.Receivable, Is.EqualTo(0m));
            Assert.That(bill.Paid, Is.False);
        });

        var income = body.Incomes[0];
        Assert.Multiple(() =>
        {
            Assert.That(income.Name, Is.EqualTo("Salario"));
            Assert.That(income.PlannedAmount, Is.EqualTo(5000m));
            Assert.That(income.ActualAmount, Is.Null);
            Assert.That(income.EffectiveAmount, Is.EqualTo(5000m));
            Assert.That(income.Received, Is.False);
        });
    }

    [Test]
    public async Task Get_BillNames_CategoryAndPersonFilled()
    {
        // Arrange
        const int year = 2025;
        const int month = 1;
        var uid = Uid("names");
        var categories = await GetDefaultCategoriesAsync(uid);
        // Use first category alphabetically; known name from the seeded defaults.
        var firstCategory = categories[0];
        var personId = await CreatePersonAsync(uid, name: "Esposa");
        await CreateSplitBillAsync(uid, firstCategory.Id, personId, name: "Internet", amount: 200m);
        await PostProjectionAsync(uid, year);

        // Act
        var (resp, body) = await GetEntriesAsync(uid, year, month);

        // Assert
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Bills, Has.Length.EqualTo(1));

        var bill = body.Bills[0];
        Assert.Multiple(() =>
        {
            Assert.That(bill.Name, Is.EqualTo("Internet"));
            Assert.That(bill.Category, Is.EqualTo(firstCategory.Name));
            Assert.That(bill.Person, Is.EqualTo("Esposa"));
            Assert.That(bill.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(bill.EffectiveAmount, Is.EqualTo(200m));
            Assert.That(bill.MyShare, Is.EqualTo(100m));
            Assert.That(bill.Receivable, Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task Get_OwnerIsolation_DoesNotSeeOtherUsersEntries()
    {
        // Arrange — user A creates bill + income and generates a projection
        const int year = 2025;
        var uidA = Uid("isolate-a");
        var uidB = Uid("isolate-b");
        var categoriesA = await GetDefaultCategoriesAsync(uidA);
        await CreateRecurringBillAsync(uidA, categoriesA[0].Id);
        await CreateRecurringIncomeAsync(uidA);
        await PostProjectionAsync(uidA, year);

        // Act — user B queries the same year/month (B has no entries)
        // Ensure B is provisioned (triggers default categories for B)
        await GetDefaultCategoriesAsync(uidB);
        var (resp, body) = await GetEntriesAsync(uidB, year, 1);

        // Assert — B sees empty lists; A's entries are invisible
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Bills, Is.Empty);
            Assert.That(body.Incomes, Is.Empty);
        });
    }

    [Test]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/entries?year=2025&month=1");

        // Act
        using var response = await _client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(0)]
    [TestCase(13)]
    public async Task Get_InvalidMonth_ReturnsBadRequest(int invalidMonth)
    {
        // Arrange — must include a valid token since auth middleware runs before the handler
        var uid = Uid($"bad-month-{invalidMonth}");
        using var req = Req(HttpMethod.Get, $"/api/entries?year=2025&month={invalidMonth}", uid);

        // Act
        using var response = await _client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
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
        decimal MyShare, decimal Receivable,
        decimal IncomesPlanned, decimal IncomesEffective,
        decimal SaldoPrevisto, decimal SaldoReal);

    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
}
