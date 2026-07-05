using Application.Abstractions.Services;
using BillsBackend.Api.Contracts;
using BillsBackend.Api.Domain;
using BillsBackend.Api.Identity;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Endpoints;

internal static class ReceivablesEndpoints
{
    public static RouteGroupBuilder MapReceivablesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/receivables/month", GetReceivablesMonth);
        group.MapPost("/receivables/{entryId:long}/mark", MarkReceivable);
        group.MapPost("/receivables/{entryId:long}/unmark", UnmarkReceivable);
        group.MapPost("/receivables/mark-batch", MarkBatch);
        group.MapGet("/receivables/history", GetReceivablesHistory);
        return group;
    }

    private static async Task<IResult> GetReceivablesMonth(
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

        var entries = await db.BillEntries
            .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value &&
                        e.SplitRatioSnapshot < 1 && e.PersonId != null)
            .ToListAsync(ct);

        var billIds = entries.Select(e => e.BillId).ToHashSet();
        var billsById = billIds.Count > 0
            ? await db.Bills
                .IgnoreQueryFilters()
                .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<long, Bill>();

        var personIds = entries.Select(e => e.PersonId!.Value).ToHashSet();
        var personsById = personIds.Count > 0
            ? await db.Persons
                .IgnoreQueryFilters()
                .Where(p => p.OwnerId == appUser.Id && personIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<long, Person>();

        var byPerson = entries
            .GroupBy(e => e.PersonId!.Value)
            .Select(g =>
            {
                var items = g
                    .OrderBy(e => e.Id)
                    .Select(e => new ReceivableItemDto(
                        e.Id, billsById[e.BillId].Name,
                        EntryCalculations.Receivable(
                            EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot),
                        e.Received))
                    .ToList();

                var totalDevido = items.Sum(i => i.Receivable);
                var jaRecebido = items.Where(i => i.Received).Sum(i => i.Receivable);
                var pendente = items.Where(i => !i.Received).Sum(i => i.Receivable);

                return new PersonReceivablesDto(g.Key, personsById[g.Key].Name, totalDevido, jaRecebido, pendente, items);
            })
            .OrderBy(p => p.Name)
            .ToList();

        var totalPendenteGeral = byPerson.Sum(p => p.Pendente);

        return Results.Ok(new ReceivablesMonthDto(year.Value, month.Value, byPerson, totalPendenteGeral));
    }

    // Marks the split portion of a bill entry as received from the other person. Idempotent —
    // marking an already-received entry again simply re-applies the same received date. Never
    // touches Paid/PaidDate, which track the independent fact that the owner paid the bill.
    private static async Task<IResult> MarkReceivable(
        long entryId,
        MarkReceivableRequest req,
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

        var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry is null)
            return Results.NotFound();

        if (entry.SplitRatioSnapshot == 1)
            return Results.BadRequest("This entry has no split; it is not a receivable.");

        var receivedAt = req.ReceivedDate.HasValue
            ? new DateTimeOffset(req.ReceivedDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : timeProvider.GetUtcNow();

        entry.MarkReceived(receivedAt);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToBillEntryDto(entry));
    }

    // Reverses a prior mark-as-received. Never touches Paid/PaidDate.
    private static async Task<IResult> UnmarkReceivable(
        long entryId,
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

        var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry is null)
            return Results.NotFound();

        entry.UnmarkReceived();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToBillEntryDto(entry));
    }

    // Marks several bill entries as received in one transaction. All-or-nothing: every id must
    // exist, belong to the caller (the BillEntries query filter already scopes reads to the
    // current owner, so a foreign/unknown id is simply absent from the fetched set), and be an
    // actual receivable (SplitRatioSnapshot < 1). If any id fails these checks, nothing is marked.
    private static async Task<IResult> MarkBatch(
        MarkBatchRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (req.EntryIds is null || req.EntryIds.Count == 0)
            return Results.BadRequest("EntryIds must contain at least one id.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var entryIds = req.EntryIds.ToHashSet();
        var entries = await db.BillEntries
            .Where(e => entryIds.Contains(e.Id))
            .ToListAsync(ct);

        if (entries.Count != entryIds.Count || entries.Any(e => e.SplitRatioSnapshot == 1))
            return Results.BadRequest("One or more entries are invalid, not owned by you, or not a receivable.");

        var receivedAt = req.ReceivedDate.HasValue
            ? new DateTimeOffset(req.ReceivedDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : timeProvider.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var entry in entries)
            entry.MarkReceived(receivedAt);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new MarkBatchResponse(entries.Count));
    }

    // Returns the receivable history for a single person: item-level rows plus aggregates computed
    // over whatever period/status filter was applied. personId must belong to the caller — Persons
    // is already owner-filtered, so a null lookup naturally covers "not found" and "not yours" alike.
    private static async Task<IResult> GetReceivablesHistory(
        long? personId,
        int? fromYear,
        int? fromMonth,
        int? toYear,
        int? toMonth,
        string? status,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        if (personId is null)
            return Results.BadRequest("personId is required.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == personId.Value, ct);
        if (person is null)
            return Results.NotFound();

        var entries = await db.BillEntries
            .Where(e => e.PersonId == personId.Value && e.SplitRatioSnapshot < 1)
            .ToListAsync(ct);

        if (fromYear.HasValue && fromMonth.HasValue)
        {
            entries = entries
                .Where(e => EntryCalculations.IsInForwardRange(e.RefYear, e.RefMonth, fromYear.Value, fromMonth.Value))
                .ToList();
        }

        if (toYear.HasValue && toMonth.HasValue)
        {
            // Reuses IsInForwardRange the other way round: "entry is at or before (toYear, toMonth)".
            entries = entries
                .Where(e => EntryCalculations.IsInForwardRange(toYear.Value, toMonth.Value, e.RefYear, e.RefMonth))
                .ToList();
        }

        // Anything other than "received"/"pending" (including a missing or unrecognized value) is
        // treated as "all" — the simplest, most defensive default that never rejects a valid request.
        entries = status switch
        {
            "received" => entries.Where(e => e.Received).ToList(),
            "pending" => entries.Where(e => !e.Received).ToList(),
            _ => entries,
        };

        var billIds = entries.Select(e => e.BillId).ToHashSet();
        var billsById = billIds.Count > 0
            ? await db.Bills
                .IgnoreQueryFilters()
                .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<long, Bill>();

        var items = entries
            .Select(e => new ReceivablesHistoryItemDto(
                e.Id, billsById[e.BillId].Name, e.RefYear, e.RefMonth,
                EntryCalculations.Receivable(
                    EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot),
                e.Received, e.ReceivedDate))
            .OrderByDescending(i => i.Year)
            .ThenByDescending(i => i.Month)
            .ToList();

        var totalDevido = items.Sum(i => i.Receivable);
        var totalRecebido = items.Where(i => i.Received).Sum(i => i.Receivable);
        var totalPendente = items.Where(i => !i.Received).Sum(i => i.Receivable);

        var totals = new ReceivablesHistoryTotalsDto(totalDevido, totalRecebido, totalPendente);

        return Results.Ok(new ReceivablesHistoryDto(person.Id, person.Name, totals, items));
    }

    private static BillEntryCreatedDto ToBillEntryDto(BillEntry e) => new(
        e.Id, e.BillId, e.RefYear, e.RefMonth,
        e.PlannedAmount, e.ActualAmount, e.SplitRatioSnapshot, e.PersonId,
        e.Paid, e.PaidDate, e.Received, e.ReceivedDate);
}
