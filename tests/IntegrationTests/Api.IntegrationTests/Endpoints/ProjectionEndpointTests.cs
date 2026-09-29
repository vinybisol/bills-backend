using System.Net;
using System.Net.Http.Json;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests.Endpoints;

/// <summary>
/// Integration tests for <c>POST /api/v1/projection/{year}</c>, covering authentication, year-range
/// validation, entry generation with snapshots, idempotency, inactive/one-off templates and owner
/// isolation. Each test authenticates as a fresh Firebase uid.
/// </summary>
[TestFixture]
public sealed class ProjectionEndpointTests : IntegrationTestBase
{
    private const int Year = 2025;

    // --- Auth ---

    [Test]
    public async Task Post_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        using var response = await Client.PostAsync($"/api/v1/projection/{Year}", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // --- Validation ---

    [TestCase(1999)]
    [TestCase(2101)]
    [TestCase(0)]
    public async Task Post_YearOutOfRange_ReturnsValidationProblem(int year)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateRecurringBillAsync(client, categoryIds[0]);

        // Act
        using var response = await PostProjectionRawAsync(client, year);

        // Assert
        await AssertValidationProblemAsync(response, "year");
        Assert.That(await CountBillEntriesAsync(client, year), Is.Zero);
    }

    [TestCase(2000)]
    [TestCase(2100)]
    public async Task Post_YearAtBoundary_ReturnsOk(int year)
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        var result = await PostProjectionAsync(client, year);

        // Assert
        Assert.That(result, Is.EqualTo(new ProjectionResultDto(year, 0, 0, 0)));
    }

    [Test]
    public async Task Post_NonNumericYear_ReturnsNotFound()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.PostAsync("/api/v1/projection/abc", null);

        // Assert — the {year:int} route constraint does not match
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Generation ---

    [Test]
    public async Task Post_ValidYear_CreatesEntriesAndReturnsCorrectCounts()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateRecurringBillAsync(client, categoryIds[0], "Aluguel");
        await CreateRecurringBillAsync(client, categoryIds[1], "Internet");
        await CreateRecurringIncomeAsync(client, "Salario");

        // Act
        using var response = await PostProjectionRawAsync(client, Year);

        // Assert — 2 recurring bills x 12 months = 24 bill entries; 1 income x 12 = 12 income entries
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<ProjectionResultDto>();
        Assert.That(result, Is.EqualTo(new ProjectionResultDto(Year, 24, 12, 0)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CountBillEntriesAsync(client, Year), Is.EqualTo(24));
            Assert.That(await CountIncomeEntriesAsync(client, Year), Is.EqualTo(12));
        }
    }

    [Test]
    public async Task Post_NoTemplates_ReturnsOkWithZeroCounts()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();

        // Act
        var result = await PostProjectionAsync(client, Year);

        // Assert
        Assert.That(result, Is.EqualTo(new ProjectionResultDto(Year, 0, 0, 0)));
    }

    [Test]
    public async Task Post_SharedBillAndIncome_EntriesCarrySnapshotOfTemplateValues()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        var personId = await CreatePersonAsync(client);
        var bill = await CreateRecurringBillAsync(client, categoryIds[0], "Internet", 200m, splitRatio: 0.5m, personId: personId);
        var income = await CreateRecurringIncomeAsync(client, "Salario", 5000m);

        // Act
        await PostProjectionAsync(client, Year);

        // Change the templates afterwards: projected entries must not follow the new values.
        using (var update = await client.PutAsJsonAsync($"/api/v1/bills/{bill.Id}",
                   new { name = "Internet", categoryId = categoryIds[0], kind = "recurring", defaultAmount = 999m, splitRatio = 1m, personId = (long?)null }))
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (var update = await client.PutAsJsonAsync($"/api/v1/incomes/{income.Id}",
                   new { name = "Salario", kind = "recurring", defaultAmount = 1m }))
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Assert
        var ownerId = await GetOwnerIdAsync(client);
        await WithOwnerDbAsync(ownerId, async db =>
        {
            var billEntries = await db.BillEntries.Where(e => e.RefYear == Year).ToListAsync();
            var incomeEntries = await db.IncomeEntries.Where(e => e.RefYear == Year).ToListAsync();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(billEntries.Select(e => e.RefMonth), Is.EquivalentTo(Enumerable.Range(1, 12)));
                Assert.That(billEntries.Select(e => e.OwnerId), Is.All.EqualTo(ownerId));
                Assert.That(billEntries.Select(e => e.PlannedAmount), Is.All.EqualTo(200m));
                Assert.That(billEntries.Select(e => e.SplitRatioSnapshot), Is.All.EqualTo(0.5m));
                Assert.That(billEntries.Select(e => e.PersonId), Is.All.EqualTo(personId));
                Assert.That(billEntries.Select(e => e.Paid), Is.All.False);
                Assert.That(incomeEntries.Select(e => e.RefMonth), Is.EquivalentTo(Enumerable.Range(1, 12)));
                Assert.That(incomeEntries.Select(e => e.PlannedAmount), Is.All.EqualTo(5000m));
                Assert.That(incomeEntries.Select(e => e.Received), Is.All.False);
            }
        });
    }

    [Test]
    public async Task Post_InactiveAndOneOffTemplates_AreIgnored()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateRecurringBillAsync(client, categoryIds[0], "Ativa");
        var deletedBill = await CreateRecurringBillAsync(client, categoryIds[1], "Removida");
        await CreateRecurringBillAsync(client, categoryIds[0], "Avulsa", kind: "one_off");
        await CreateRecurringIncomeAsync(client, "Salario");
        var deletedIncome = await CreateRecurringIncomeAsync(client, "Antiga");
        await CreateRecurringIncomeAsync(client, "Bonus", kind: "one_off");

        using (var del = await client.DeleteAsync($"/api/v1/bills/{deletedBill.Id}"))
            Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using (var del = await client.DeleteAsync($"/api/v1/incomes/{deletedIncome.Id}"))
            Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Act
        var result = await PostProjectionAsync(client, Year);

        // Assert — only the active recurring bill and income are projected
        Assert.That(result, Is.EqualTo(new ProjectionResultDto(Year, 12, 12, 0)));
    }

    // --- Idempotency ---

    [Test]
    public async Task Post_CalledTwice_DoesNotDuplicateEntries()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateRecurringBillAsync(client, categoryIds[0]);
        await CreateRecurringIncomeAsync(client);

        // Act
        var first = await PostProjectionAsync(client, Year);
        var second = await PostProjectionAsync(client, Year);

        // Assert — first call creates all entries; second call skips all of them
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(new ProjectionResultDto(Year, 12, 12, 0)));
            Assert.That(second, Is.EqualTo(new ProjectionResultDto(Year, 0, 0, 24)));
            Assert.That(await CountBillEntriesAsync(client, Year), Is.EqualTo(12));
            Assert.That(await CountIncomeEntriesAsync(client, Year), Is.EqualTo(12));
        }
    }

    [Test]
    public async Task Post_NewTemplateAfterProjection_CreatesOnlyItsEntries()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateRecurringBillAsync(client, categoryIds[0], "Aluguel");
        await PostProjectionAsync(client, Year);
        await CreateRecurringBillAsync(client, categoryIds[1], "Internet");

        // Act
        var result = await PostProjectionAsync(client, Year);

        // Assert
        Assert.That(result, Is.EqualTo(new ProjectionResultDto(Year, 12, 0, 12)));
        Assert.That(await CountBillEntriesAsync(client, Year), Is.EqualTo(24));
    }

    [Test]
    public async Task Post_DifferentYear_ProjectsIndependently()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateRecurringBillAsync(client, categoryIds[0]);
        await PostProjectionAsync(client, Year);

        // Act
        var result = await PostProjectionAsync(client, Year + 1);

        // Assert
        Assert.That(result, Is.EqualTo(new ProjectionResultDto(Year + 1, 12, 0, 0)));
    }

    [Test]
    public async Task Post_PaidEntryBeforeSecondProjection_RemainsUnchanged()
    {
        // Arrange
        using var client = CreateAuthenticatedClient();
        var categoryIds = await GetDefaultCategoryIdsAsync(client);
        await CreateRecurringBillAsync(client, categoryIds[0], amount: 1000m);
        await PostProjectionAsync(client, Year);
        var ownerId = await GetOwnerIdAsync(client);

        long entryId = 0;
        await WithOwnerDbAsync(ownerId, async db =>
        {
            var entry = await db.BillEntries.FirstAsync(e => e.RefYear == Year && e.RefMonth == 1);
            entryId = entry.Id;
            entry.MarkPaid(DateTimeOffset.UtcNow, 950m);
            await db.SaveChangesAsync();
        });

        // Act — second projection must skip every existing entry
        var result = await PostProjectionAsync(client, Year);

        // Assert — the paid entry keeps its frozen values
        Assert.That(result, Is.EqualTo(new ProjectionResultDto(Year, 0, 0, 12)));
        await WithOwnerDbAsync(ownerId, async db =>
        {
            var entryAfter = await db.BillEntries.SingleAsync(e => e.Id == entryId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entryAfter.Paid, Is.True);
                Assert.That(entryAfter.ActualAmount, Is.EqualTo(950m));
                Assert.That(entryAfter.PlannedAmount, Is.EqualTo(1000m));
            }
        });
    }

    // --- Owner isolation ---

    [Test]
    public async Task Post_OwnerIsolation_DoesNotGenerateForOtherOwner()
    {
        // Arrange — user A has recurring templates; user B has none
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        var categoryIdsA = await GetDefaultCategoryIdsAsync(clientA);
        await CreateRecurringBillAsync(clientA, categoryIdsA[0]);
        await CreateRecurringIncomeAsync(clientA);

        // Act
        var resultB = await PostProjectionAsync(clientB, Year);

        // Assert — B's projection sees none of A's templates and A gets no entries from it
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resultB, Is.EqualTo(new ProjectionResultDto(Year, 0, 0, 0)));
            Assert.That(await CountBillEntriesAsync(clientA, Year), Is.Zero);
            Assert.That(await CountIncomeEntriesAsync(clientA, Year), Is.Zero);
        }
    }

    [Test]
    public async Task Post_OtherOwnerAlreadyProjected_StillCreatesOwnEntries()
    {
        // Arrange — A projects first; B's idempotency check must not see A's entries
        using var clientA = CreateAuthenticatedClient();
        using var clientB = CreateAuthenticatedClient();
        await CreateRecurringBillAsync(clientA, (await GetDefaultCategoryIdsAsync(clientA))[0]);
        await CreateRecurringBillAsync(clientB, (await GetDefaultCategoryIdsAsync(clientB))[0]);
        await PostProjectionAsync(clientA, Year);

        // Act
        var resultB = await PostProjectionAsync(clientB, Year);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resultB, Is.EqualTo(new ProjectionResultDto(Year, 12, 0, 0)));
            Assert.That(await CountBillEntriesAsync(clientA, Year), Is.EqualTo(12));
            Assert.That(await CountBillEntriesAsync(clientB, Year), Is.EqualTo(12));
        }
    }

    // --- Helpers ---

    private static Task<HttpResponseMessage> PostProjectionRawAsync(HttpClient client, int year) =>
        client.PostAsync($"/api/v1/projection/{year}", null);

    private static async Task<ProjectionResultDto> PostProjectionAsync(HttpClient client, int year)
    {
        using var response = await PostProjectionRawAsync(client, year);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<ProjectionResultDto>())!;
    }

    // Every new user has default categories seeded on first authenticated request.
    private static async Task<long[]> GetDefaultCategoryIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/categories");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await response.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Has.Length.GreaterThanOrEqualTo(2), "Expected seeded default categories.");
        return [.. dtos!.Select(c => c.Id)];
    }

    private static async Task<long> CreatePersonAsync(HttpClient client, string name = "Parceiro")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/persons", new { name });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<PersonDto>())!.Id;
    }

    private static async Task<BillDto> CreateRecurringBillAsync(
        HttpClient client, long categoryId, string name = "Aluguel", decimal amount = 1000m,
        string kind = "recurring", decimal splitRatio = 1m, long? personId = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bills",
            new { name, categoryId, kind, defaultAmount = amount, splitRatio, personId });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<BillDto>())!;
    }

    private static async Task<IncomeDto> CreateRecurringIncomeAsync(
        HttpClient client, string name = "Salario", decimal amount = 5000m, string kind = "recurring")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/incomes", new { name, kind, defaultAmount = amount });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await response.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    private static async Task<long> GetOwnerIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/me");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<MeDto>())!.Id;
    }

    // Runs the action against a fresh DbContext scoped to the given owner (global query filter).
    private async Task WithOwnerDbAsync(long ownerId, Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentOwner>().SetCurrentOwnerId(ownerId);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<int> CountBillEntriesAsync(HttpClient client, int year)
    {
        var ownerId = await GetOwnerIdAsync(client);
        var count = 0;
        await WithOwnerDbAsync(ownerId, async db => count = await db.BillEntries.CountAsync(e => e.RefYear == year));
        return count;
    }

    private async Task<int> CountIncomeEntriesAsync(HttpClient client, int year)
    {
        var ownerId = await GetOwnerIdAsync(client);
        var count = 0;
        await WithOwnerDbAsync(ownerId, async db => count = await db.IncomeEntries.CountAsync(e => e.RefYear == year));
        return count;
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
            Assert.That(problem.Errors.Values, Has.All.Not.Empty);
        }
    }

    private sealed record ProjectionResultDto(int Year, int BillEntriesCreated, int IncomeEntriesCreated, int Skipped);
    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record PersonDto(long Id, string Name);
    private sealed record MeDto(long Id, string Name, string? Email);
    private sealed record ValidationProblemBody(int Status, Dictionary<string, string[]> Errors);
}