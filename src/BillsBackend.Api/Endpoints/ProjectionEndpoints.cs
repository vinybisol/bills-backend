using BillsBackend.Api.Contracts;
using BillsBackend.Api.Data;
using BillsBackend.Api.Domain;
using BillsBackend.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Endpoints;

internal static class ProjectionEndpoints
{
    public static RouteGroupBuilder MapProjectionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/projection/{year:int}", CreateProjection);
        return group;
    }

    // Generates annual projected entries for every active recurring bill and income template
    // owned by the authenticated user. Idempotent: existing entries (identified by bill/income
    // id + year + month) are skipped, so calling the endpoint twice for the same year is safe.
    private static async Task<IResult> CreateProjection(
        int year,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (year < 2000 || year > 2100)
            return Results.BadRequest("Year must be between 2000 and 2100.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.Id = appUser.Id;

        // Fetch only recurring active bill/income templates (global query filter applies active + owner_id).
        var recurringBills = await db.Bills
            .Where(b => b.Kind == BillKind.Recurring)
            .ToListAsync(ct);

        var recurringIncomes = await db.Incomes
            .Where(i => i.Kind == IncomeKind.Recurring)
            .ToListAsync(ct);

        // Fetch already-created entries for the year so subsequent calls are idempotent.
        var existingBillEntries = await db.BillEntries
            .Where(e => e.RefYear == year)
            .Select(e => new { e.BillId, e.RefMonth })
            .ToListAsync(ct);

        var existingIncomeEntries = await db.IncomeEntries
            .Where(e => e.RefYear == year)
            .Select(e => new { e.IncomeId, e.RefMonth })
            .ToListAsync(ct);

        // ValueTuple structural equality, so Contains checks are O(1) via HashSet.
        var existingBillSet = existingBillEntries
            .Select(e => (e.BillId, e.RefMonth))
            .ToHashSet();

        var existingIncomeSet = existingIncomeEntries
            .Select(e => (e.IncomeId, e.RefMonth))
            .ToHashSet();

        var now = timeProvider.GetUtcNow();
        int billEntriesCreated = 0, incomeEntriesCreated = 0, skipped = 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var bill in recurringBills)
        {
            for (var month = 1; month <= 12; month++)
            {
                if (existingBillSet.Contains((bill.Id, month)))
                {
                    skipped++;
                    continue;
                }

                db.BillEntries.Add(BillEntry.Create(appUser.Id, bill.Id, year, month, bill.DefaultAmount, bill.SplitRatio, bill.PersonId, now));
                billEntriesCreated++;
            }
        }

        foreach (var income in recurringIncomes)
        {
            for (var month = 1; month <= 12; month++)
            {
                if (existingIncomeSet.Contains((income.Id, month)))
                {
                    skipped++;
                    continue;
                }

                db.IncomeEntries.Add(IncomeEntry.Create(appUser.Id, income.Id, year, month, income.DefaultAmount, now));
                incomeEntriesCreated++;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new ProjectionResult(year, billEntriesCreated, incomeEntriesCreated, skipped));
    }
}
