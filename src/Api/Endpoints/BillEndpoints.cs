using Api.Identity;
using Application.Abstractions.Services;
using Api.Contracts;
using Domain.Calculations;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

internal static class BillEndpoints
{
    public static RouteGroupBuilder MapBillEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/bills", CreateBill);
        group.MapGet("/bills", ListBills);
        group.MapPut("/bills/{id:long}", UpdateBill);
        group.MapDelete("/bills/{id:long}", DeleteBill);
        group.MapPost("/bills/{billId:long}/recalculate", RecalculateBill);
        group.MapGet("/bills/{billId:long}/history", GetBillHistory);
        return group;
    }

    private static async Task<IResult> CreateBill(
        CreateBillRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");

        if (req.DefaultAmount < 0)
            return Results.BadRequest("Default amount must be zero or greater.");

        if (req.SplitRatio < 0m || req.SplitRatio > 1m)
            return Results.BadRequest("SplitRatio must be between 0 and 1.");

        if (req.SplitRatio < 1m && req.PersonId is null)
            return Results.BadRequest("PersonId is required when SplitRatio is less than 1.");

        if (req.SplitRatio == 1m && req.PersonId is not null)
            return Results.BadRequest("PersonId must be null when SplitRatio is 1.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return Results.NotFound("Category not found.");

        if (req.PersonId is not null && !await db.Persons.AnyAsync(p => p.Id == req.PersonId.Value, ct))
            return Results.NotFound("Person not found.");

        var bill = Bill.Create(appUser.Id, req.Name, req.CategoryId, req.Kind, req.DefaultAmount, req.SplitRatio, req.PersonId, timeProvider.GetUtcNow());
        db.Bills.Add(bill);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/bills/{bill.Id}", new BillDto(bill.Id, bill.Name, bill.CategoryId, bill.Kind, bill.DefaultAmount, bill.SplitRatio, bill.PersonId));
    }

    private static async Task<IResult> ListBills(
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var bills = await db.Bills
            .OrderBy(b => b.Name)
            .Select(b => new BillDto(b.Id, b.Name, b.CategoryId, b.Kind, b.DefaultAmount, b.SplitRatio, b.PersonId))
            .ToListAsync(ct);

        return Results.Ok(bills);
    }

    private static async Task<IResult> UpdateBill(
        long id,
        UpdateBillRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");

        if (req.DefaultAmount < 0)
            return Results.BadRequest("Default amount must be zero or greater.");

        if (req.SplitRatio < 0m || req.SplitRatio > 1m)
            return Results.BadRequest("SplitRatio must be between 0 and 1.");

        if (req.SplitRatio < 1m && req.PersonId is null)
            return Results.BadRequest("PersonId is required when SplitRatio is less than 1.");

        if (req.SplitRatio == 1m && req.PersonId is not null)
            return Results.BadRequest("PersonId must be null when SplitRatio is 1.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bill is null)
            return Results.NotFound();

        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return Results.NotFound("Category not found.");

        if (req.PersonId is not null && !await db.Persons.AnyAsync(p => p.Id == req.PersonId.Value, ct))
            return Results.NotFound("Person not found.");

        bill.Update(req.Name, req.CategoryId, req.Kind, req.DefaultAmount, req.SplitRatio, req.PersonId);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new BillDto(bill.Id, bill.Name, bill.CategoryId, bill.Kind, bill.DefaultAmount, bill.SplitRatio, bill.PersonId));
    }

    private static async Task<IResult> DeleteBill(
        long id,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bill is null)
            return Results.NotFound();

        bill.Deactivate();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // Recalculates bill default amount and propagates to unpaid future entries.
    // Paid entries in range are skipped (immutability); entries before fromMonth are untouched.
    private static async Task<IResult> RecalculateBill(
        long billId,
        RecalculateRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        if (req.FromMonth < 1 || req.FromMonth > 12)
            return Results.BadRequest("FromMonth must be between 1 and 12.");

        if (req.NewAmount < 0)
            return Results.BadRequest("NewAmount must be zero or greater.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == billId, ct);
        if (bill is null)
            return Results.NotFound();

        // Capture locals so EF Core can translate the predicate to SQL.
        var fromYear = req.FromYear;
        var fromMonth = req.FromMonth;

        var entriesInRange = await db.BillEntries
            .Where(e => e.BillId == billId &&
                        (e.RefYear > fromYear || (e.RefYear == fromYear && e.RefMonth >= fromMonth)))
            .ToListAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        bill.Recalculate(req.NewAmount);

        var result = BillRecalculation.ApplyToEntries(entriesInRange, req.NewAmount);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new RecalculateResponse(billId, result.UpdatedEntries, result.SkippedPaid, req.NewAmount));
    }

    private static async Task<IResult> GetBillHistory(
        long billId,
        int? fromYear,
        int? fromMonth,
        int? toYear,
        int? toMonth,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        // IgnoreQueryFilters + manual OwnerId check: the bill template may have been deactivated
        // since some of its entries were created, but its history must still resolve.
        var bill = await db.Bills
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == billId && b.OwnerId == appUser.Id, ct);
        if (bill is null)
            return Results.NotFound();

        var category = await db.Categories
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == bill.CategoryId && c.OwnerId == appUser.Id, ct);

        var person = bill.PersonId.HasValue
            ? await db.Persons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == bill.PersonId.Value && p.OwnerId == appUser.Id, ct)
            : null;

        var entries = await db.BillEntries
            .Where(e => e.BillId == billId)
            .ToListAsync(ct);

        var historyItems = BillHistoryCalculations.BuildItems(
            entries.Where(e => EntryCalculations.IsInPeriod(e.RefYear, e.RefMonth, fromYear, fromMonth, toYear, toMonth)));

        var items = historyItems
            .Select(i => new BillHistoryItemDto(
                i.Year, i.Month, i.PlannedAmount, i.ActualAmount, i.Effective, i.MyShare,
                i.Paid, i.PaidDate,
                i.Variation is null ? null : new BillHistoryVariationDto(i.Variation.Value.Abs, i.Variation.Value.Pct)))
            .ToList();

        var historySummary = BillHistoryCalculations.Summarize(historyItems);
        var summary = new BillHistorySummaryDto(
            historySummary.AverageEffective, historySummary.MinEffective,
            historySummary.MaxEffective, historySummary.TotalPaidMyShare);

        return Results.Ok(new BillHistoryDto(
            bill.Id, bill.Name, category.Name, bill.SplitRatio, person?.Name, summary, items));
    }
}
