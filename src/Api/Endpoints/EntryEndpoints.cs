using Api.Identity;
using Application.Abstractions.Services;
using BillsBackend.Api.Contracts;
using BillsBackend.Api.Domain;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

internal static class EntryEndpoints
{
    public static RouteGroupBuilder MapEntryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/entries", GetEntries);
        group.MapPost("/entries/bill", CreateBillEntry);
        group.MapPost("/entries/income", CreateIncomeEntry);
        group.MapDelete("/entries/bill/{id:long}", DeleteBillEntry);
        group.MapDelete("/entries/income/{id:long}", DeleteIncomeEntry);
        group.MapMethods("/entries/bill/{id:long}", ["PATCH"], PatchBillEntry);
        group.MapPost("/entries/bill/{id:long}/pay", PayBillEntry);
        group.MapPost("/entries/bill/{id:long}/unpay", UnpayBillEntry);
        group.MapMethods("/entries/income/{id:long}", ["PATCH"], PatchIncomeEntry);
        group.MapPost("/entries/income/{id:long}/receive", ReceiveIncomeEntry);
        group.MapPost("/entries/income/{id:long}/unreceive", UnreceiveIncomeEntry);
        return group;
    }

    private static async Task<IResult> GetEntries(
        int? year,
        int? month,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        if (year is null || month is null || month < 1 || month > 12)
            return Results.BadRequest("year and month are required; month must be between 1 and 12.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        // Fetch entries for the requested month. The global query filter already scopes
        // BillEntries and IncomeEntries to the current owner (owner_id only, no active flag).
        var billEntries = await db.BillEntries
            .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
            .ToListAsync(ct);

        var incomeEntries = await db.IncomeEntries
            .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
            .ToListAsync(ct);

        // Fetch reference data with IgnoreQueryFilters so inactive bills/categories/persons
        // that were snapshotted at projection time still resolve. Filter by OwnerId manually.
        var billIds = billEntries.Select(e => e.BillId).ToHashSet();
        var billsById = billIds.Count > 0
            ? await db.Bills
                .IgnoreQueryFilters()
                .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<long, Bill>();

        var categoryIds = billsById.Values.Select(b => b.CategoryId).ToHashSet();
        var categoriesById = categoryIds.Count > 0
            ? await db.Categories
                .IgnoreQueryFilters()
                .Where(c => c.OwnerId == appUser.Id && categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct)
            : new Dictionary<long, Category>();

        // PersonId on the BillEntry is the snapshotted value at projection time.
        var personIds = billEntries
            .Where(e => e.PersonId.HasValue)
            .Select(e => e.PersonId!.Value)
            .ToHashSet();
        var personsById = personIds.Count > 0
            ? await db.Persons
                .IgnoreQueryFilters()
                .Where(p => p.OwnerId == appUser.Id && personIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<long, Person>();

        var incomeIds = incomeEntries.Select(e => e.IncomeId).ToHashSet();
        var incomesById = incomeIds.Count > 0
            ? await db.Incomes
                .IgnoreQueryFilters()
                .Where(i => i.OwnerId == appUser.Id && incomeIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct)
            : new Dictionary<long, Income>();

        // Build bill DTOs with derived values; sort by category then name in-memory.
        var billDtos = billEntries
            .Select(e =>
            {
                var bill = billsById[e.BillId];
                var category = categoriesById[bill.CategoryId];
                var personName = e.PersonId.HasValue && personsById.TryGetValue(e.PersonId.Value, out var p)
                    ? p.Name
                    : null;
                var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
                return new BillEntryDto(
                    e.Id, e.BillId, bill.Name, category.Name,
                    e.PlannedAmount, e.ActualAmount, e.SplitRatioSnapshot, personName,
                    effective,
                    EntryCalculations.MyShare(effective, e.SplitRatioSnapshot),
                    EntryCalculations.Receivable(effective, e.SplitRatioSnapshot),
                    e.Paid, e.PaidDate, e.Received, e.ReceivedDate);
            })
            .OrderBy(d => d.Category)
            .ThenBy(d => d.Name)
            .ToList();

        // Build income DTOs with derived values.
        var incomeDtos = incomeEntries
            .Select(e =>
            {
                var income = incomesById[e.IncomeId];
                var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
                return new IncomeEntryDto(
                    e.Id, e.IncomeId, income.Name,
                    e.PlannedAmount, e.ActualAmount,
                    effective, e.Received, e.ReceivedDate);
            })
            .ToList();

        // --- Totals ---
        var billsPlanned = billDtos.Sum(d => d.PlannedAmount);
        var billsEffective = billDtos.Sum(d => d.EffectiveAmount);
        var totalMyShare = billDtos.Sum(d => d.MyShare);
        // Receivable is split between what's still pending and what has already been received;
        // pending + received always equals the total split amount owed by other people.
        var receivablePending = billDtos.Where(d => !d.Received).Sum(d => d.Receivable);
        var receivableReceived = billDtos.Where(d => d.Received).Sum(d => d.Receivable);
        var paidFull = billDtos.Where(d => d.Paid).Sum(d => d.EffectiveAmount);
        var incomesPlanned = incomeDtos.Sum(d => d.PlannedAmount);
        var incomesEffective = incomeDtos.Sum(d => d.EffectiveAmount);
        // incomesReceived: effective amount of only the incomes actually marked as received — unlike
        // incomesEffective, this does not fall back to the planned amount for entries not yet received.
        var incomesReceived = incomeDtos.Where(d => d.Received).Sum(d => d.EffectiveAmount);

        // saldoPrevistoOtimista: how much I expect to net if everyone pays what they owe — planned
        // income minus my planned share of each bill.
        var saldoPrevistoOtimista = incomesPlanned - billDtos.Sum(d => d.PlannedAmount * d.SplitRatio);

        // saldoPrevistoPiorCaso: same as above, but assumes the pending receivable is never paid back.
        var saldoPrevistoPiorCaso = saldoPrevistoOtimista - receivablePending;

        // saldoRealizado: actual cash — received income plus received reimbursements, minus the full
        // (not myShare) amount actually paid for bills.
        var saldoRealizado = incomesReceived + receivableReceived - paidFull;

        var totals = new MonthTotalsDto(
            billsPlanned, billsEffective, totalMyShare, receivablePending, receivableReceived,
            receivablePending, receivableReceived, paidFull,
            incomesPlanned, incomesEffective, incomesReceived,
            saldoPrevistoOtimista, saldoRealizado,
            saldoPrevistoOtimista, saldoPrevistoPiorCaso, saldoRealizado);

        return Results.Ok(new MonthEntriesDto(year.Value, month.Value, billDtos, incomeDtos, totals));
    }

    // Creates a bill_entry for a one_off bill template in the requested month.
    // Snapshots planned_amount, split_ratio and person_id from the template at creation time.
    // Returns 409 if an entry already exists for the same template and month (UNIQUE constraint).
    private static async Task<IResult> CreateBillEntry(
        CreateBillEntryRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (req.Month < 1 || req.Month > 12)
            return Results.BadRequest("Month must be between 1 and 12.");

        if (req.PlannedAmount.HasValue && req.PlannedAmount.Value < 0)
            return Results.BadRequest("PlannedAmount must be zero or greater.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == req.BillId, ct);
        if (bill is null)
            return Results.NotFound("Bill not found.");

        if (bill.Kind != BillKindEnum.OneOff)
            return Results.BadRequest("Only one_off bill templates can be used to create entries via this endpoint.");

        var plannedAmount = req.PlannedAmount ?? bill.DefaultAmount;
        var entry = BillEntry.Create(appUser.Id, bill.Id, req.Year, req.Month, plannedAmount, bill.SplitRatio, bill.PersonId, timeProvider.GetUtcNow());
        db.BillEntries.Add(entry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Results.Conflict("A one-off entry for this bill and month already exists.");
        }

        return Results.Created($"/api/v1/entries/bill/{entry.Id}", new BillEntryCreatedDto(
            entry.Id, entry.BillId, entry.RefYear, entry.RefMonth,
            entry.PlannedAmount, entry.ActualAmount, entry.SplitRatioSnapshot, entry.PersonId,
            entry.Paid, entry.PaidDate, entry.Received, entry.ReceivedDate));
    }

    // Creates an income_entry for a one_off income template in the requested month.
    // Returns 409 if an entry already exists for the same template and month (UNIQUE constraint).
    private static async Task<IResult> CreateIncomeEntry(
        CreateIncomeEntryRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (req.Month < 1 || req.Month > 12)
            return Results.BadRequest("Month must be between 1 and 12.");

        if (req.PlannedAmount.HasValue && req.PlannedAmount.Value < 0)
            return Results.BadRequest("PlannedAmount must be zero or greater.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var income = await db.Incomes.FirstOrDefaultAsync(i => i.Id == req.IncomeId, ct);
        if (income is null)
            return Results.NotFound("Income not found.");

        if (income.Kind != IncomeKindEnum.OneOff)
            return Results.BadRequest("Only one_off income templates can be used to create entries via this endpoint.");

        var plannedAmount = req.PlannedAmount ?? income.DefaultAmount;
        var entry = IncomeEntry.Create(appUser.Id, income.Id, req.Year, req.Month, plannedAmount, timeProvider.GetUtcNow());
        db.IncomeEntries.Add(entry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Results.Conflict("A one-off entry for this income and month already exists.");
        }

        return Results.Created($"/api/v1/entries/income/{entry.Id}", new IncomeEntryCreatedDto(
            entry.Id, entry.IncomeId, entry.RefYear, entry.RefMonth,
            entry.PlannedAmount, entry.ActualAmount, entry.Received, entry.ReceivedDate));
    }

    // Deletes an unpaid one-off bill_entry (hard delete — unpaid entries have no history to preserve).
    // Returns 409 if the entry has been paid (immutability: paid entries are frozen).
    private static async Task<IResult> DeleteBillEntry(
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

        var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        if (entry.Paid)
            return Results.Conflict("Cannot delete a paid bill entry.");

        db.BillEntries.Remove(entry);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // Deletes an unreceived one-off income_entry (hard delete — unreceived entries have no history to preserve).
    // Returns 409 if the entry has been received (immutability: received entries are frozen).
    private static async Task<IResult> DeleteIncomeEntry(
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

        var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        if (entry.Received)
            return Results.Conflict("Cannot delete a received income entry.");

        db.IncomeEntries.Remove(entry);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // Updates plannedAmount and/or actualAmount on an unfrozen bill entry.
    // Returns 409 if the entry is paid (frozen).
    private static async Task<IResult> PatchBillEntry(
        long id,
        PatchBillEntryRequest req,
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

        var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        if (entry.Paid)
            return Results.Conflict("Cannot edit a frozen (paid) bill entry. Unpay it first.");

        try
        {
            entry.UpdateAmounts(req.PlannedAmount, req.ActualAmount);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToBillEntryDto(entry));
    }

    // Marks a bill entry as paid. Freezes it and records the actual amount (defaults to planned).
    private static async Task<IResult> PayBillEntry(
        long id,
        PayBillEntryRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        var paidAt = req.PaidDate.HasValue
            ? new DateTimeOffset(req.PaidDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : timeProvider.GetUtcNow();

        entry.MarkPaid(paidAt, req.ActualAmount);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToBillEntryDto(entry));
    }

    // Unfreezes a paid bill entry so it can be edited again.
    private static async Task<IResult> UnpayBillEntry(
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

        var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        entry.Unfreeze();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToBillEntryDto(entry));
    }

    // Updates plannedAmount and/or actualAmount on an unfrozen income entry.
    // Returns 409 if the entry is received (frozen).
    private static async Task<IResult> PatchIncomeEntry(
        long id,
        PatchIncomeEntryRequest req,
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

        var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        if (entry.Received)
            return Results.Conflict("Cannot edit a frozen (received) income entry. Unreceive it first.");

        try
        {
            entry.UpdateAmounts(req.PlannedAmount, req.ActualAmount);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToIncomeEntryDto(entry));
    }

    // Marks an income entry as received. Freezes it and records the actual amount (defaults to planned).
    private static async Task<IResult> ReceiveIncomeEntry(
        long id,
        ReceiveIncomeEntryRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        var receivedAt = req.ReceivedDate.HasValue
            ? new DateTimeOffset(req.ReceivedDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : timeProvider.GetUtcNow();

        entry.MarkReceived(receivedAt, req.ActualAmount);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToIncomeEntryDto(entry));
    }

    // Unfreezes a received income entry so it can be edited again.
    private static async Task<IResult> UnreceiveIncomeEntry(
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

        var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return Results.NotFound();

        entry.Unfreeze();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToIncomeEntryDto(entry));
    }

    // Maps a BillEntry domain object to the shared entry DTO used by pay/unpay/patch endpoints.
    private static BillEntryCreatedDto ToBillEntryDto(BillEntry e) => new(
        e.Id, e.BillId, e.RefYear, e.RefMonth,
        e.PlannedAmount, e.ActualAmount, e.SplitRatioSnapshot, e.PersonId,
        e.Paid, e.PaidDate, e.Received, e.ReceivedDate);

    // Maps an IncomeEntry domain object to the shared entry DTO used by receive/unreceive/patch endpoints.
    private static IncomeEntryCreatedDto ToIncomeEntryDto(IncomeEntry e) => new(
        e.Id, e.IncomeId, e.RefYear, e.RefMonth,
        e.PlannedAmount, e.ActualAmount, e.Received, e.ReceivedDate);
}
