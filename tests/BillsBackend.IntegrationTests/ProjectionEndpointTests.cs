using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Integration tests for <c>POST /api/projection/{year}</c>, covering entry generation,
/// idempotency, owner isolation, and year-range validation.
/// </summary>
[TestFixture]
public sealed class ProjectionEndpointTests : IntegrationTestBase
{
    private static string Uid(string suffix) => $"firebase-projection-{suffix}";

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

    // Every new user has 7 default categories seeded on first authenticated request.
    // Fetching them avoids name conflicts with those seeded defaults.
    private async Task<long[]> GetDefaultCategoryIdsAsync(string uid)
    {
        using var req = Req(HttpMethod.Get, "/api/v1/categories", uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dtos = await resp.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.That(dtos, Is.Not.Empty, "Expected seeded default categories.");
        return dtos!.Select(c => c.Id).ToArray();
    }

    // Creates a recurring bill for the given uid and returns the created BillDto.
    private async Task<BillDto> CreateRecurringBillAsync(string uid, long categoryId, string name = "Aluguel", decimal amount = 1000m)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/bills", uid,
            new { name, categoryId, kind = "recurring", defaultAmount = amount, splitRatio = 1m, personId = (long?)null });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<BillDto>())!;
    }

    // Creates a recurring income for the given uid and returns the created IncomeDto.
    private async Task<IncomeDto> CreateRecurringIncomeAsync(string uid, string name = "Salario", decimal amount = 5000m)
    {
        using var req = ReqWithBody(HttpMethod.Post, "/api/v1/incomes", uid,
            new { name, kind = "recurring", defaultAmount = amount });
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return (await resp.Content.ReadFromJsonAsync<IncomeDto>())!;
    }

    // Calls POST /api/projection/{year} and returns the deserialized result.
    private async Task<ProjectionResultDto?> PostProjectionAsync(string uid, int year)
    {
        using var req = Req(HttpMethod.Post, $"/api/v1/projection/{year}", uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return await resp.Content.ReadFromJsonAsync<ProjectionResultDto>();
    }

    // Gets the internal app_user.id for the given Firebase uid by calling GET /me.
    private async Task<long> GetOwnerIdAsync(string uid)
    {
        using var req = Req(HttpMethod.Get, "/api/v1/me", uid);
        using var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var me = await resp.Content.ReadFromJsonAsync<MeDto>();
        return me!.Id;
    }

    // --- Tests ---

    [Test]
    public async Task Post_ValidYear_CreatesEntriesAndReturnsCorrectCounts()
    {
        // Arrange
        var uid = Uid("valid-year");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        await CreateRecurringBillAsync(uid, categoryIds[0], "Aluguel");
        await CreateRecurringBillAsync(uid, categoryIds[1], "Internet");
        await CreateRecurringIncomeAsync(uid, "Salario");

        // Act
        var result = await PostProjectionAsync(uid, 2025);

        // Assert — 2 recurring bills x 12 months = 24 bill entries; 1 income x 12 = 12 income entries
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Year, Is.EqualTo(2025));
            Assert.That(result.BillEntriesCreated, Is.EqualTo(24));
            Assert.That(result.IncomeEntriesCreated, Is.EqualTo(12));
            Assert.That(result.Skipped, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Post_CalledTwice_DoesNotDuplicateEntries()
    {
        // Arrange
        var uid = Uid("idempotent");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        await CreateRecurringBillAsync(uid, categoryIds[0]);
        await CreateRecurringIncomeAsync(uid);

        // Act
        var first = await PostProjectionAsync(uid, 2025);
        var second = await PostProjectionAsync(uid, 2025);

        // Assert — first call creates all entries; second call skips all of them
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(first!.BillEntriesCreated, Is.EqualTo(12));
            Assert.That(first.IncomeEntriesCreated, Is.EqualTo(12));
            Assert.That(first.Skipped, Is.EqualTo(0));

            Assert.That(second!.BillEntriesCreated, Is.EqualTo(0));
            Assert.That(second.IncomeEntriesCreated, Is.EqualTo(0));
            Assert.That(second.Skipped, Is.EqualTo(24));
        });
    }

    [Test]
    public async Task Post_OwnerIsolation_DoesNotGenerateForOtherOwner()
    {
        // Arrange — user A creates recurring bills; user B generates a projection
        var uidA = Uid("isolate-a");
        var uidB = Uid("isolate-b");
        var categoryIdsA = await GetDefaultCategoryIdsAsync(uidA);
        await CreateRecurringBillAsync(uidA, categoryIdsA[0]);
        await CreateRecurringIncomeAsync(uidA);

        // Act — user B has no templates, so the projection should produce nothing
        var result = await PostProjectionAsync(uidB, 2025);

        // Assert — B's projection touches only B's templates; A's bills/incomes are invisible
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.BillEntriesCreated, Is.EqualTo(0));
            Assert.That(result.IncomeEntriesCreated, Is.EqualTo(0));
            Assert.That(result.Skipped, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Post_PaidEntryBeforeSecondProjection_RemainsUnchanged()
    {
        // Arrange
        const int year = 2025;
        var uid = Uid("paid-entry");
        var categoryIds = await GetDefaultCategoryIdsAsync(uid);
        await CreateRecurringBillAsync(uid, categoryIds[0]);

        // First projection creates entries
        await PostProjectionAsync(uid, year);

        // Retrieve owner id so the scoped DbContext query filter works correctly
        var ownerId = await GetOwnerIdAsync(uid);

        // Mark the first entry as paid directly via the DbContext
        long entryId;
        {
            using var scope = Factory.Services.CreateScope();
            var co = scope.ServiceProvider.GetRequiredService<ICurrentOwner>();
            co.SetCurrentOwnerId(ownerId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.BillEntries.FirstAsync(e => e.RefYear == year);
            entryId = entry.Id;
            entry.MarkPaid(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // Act — second projection should skip all existing entries
        await PostProjectionAsync(uid, year);

        // Assert — the paid entry must still be paid after the second projection
        {
            using var scope = Factory.Services.CreateScope();
            var co = scope.ServiceProvider.GetRequiredService<ICurrentOwner>();
            co.SetCurrentOwnerId(ownerId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entryAfter = await db.BillEntries.SingleAsync(e => e.Id == entryId);
            Assert.That(entryAfter.Paid, Is.True);
        }
    }

    [Test]
    public async Task Post_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projection/2025");

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(1999)]
    [TestCase(2101)]
    public async Task Post_YearOutOfRange_ReturnsBadRequest(int year)
    {
        // Arrange — must include a valid token since auth middleware runs before the handler
        var uid = Uid($"bad-year-{year}");
        using var req = Req(HttpMethod.Post, $"/api/v1/projection/{year}", uid);

        // Act
        using var response = await Client.SendAsync(req);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // --- Local DTOs for JSON deserialization ---

    private sealed record ProjectionResultDto(int Year, int BillEntriesCreated, int IncomeEntriesCreated, int Skipped);
    private sealed record BillDto(long Id, string Name, long CategoryId, string Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);
    private sealed record IncomeDto(long Id, string Name, string Kind, decimal DefaultAmount);
    private sealed record CategoryDto(long Id, string Name);
    private sealed record MeDto(long Id, string Name, string? Email);
}
