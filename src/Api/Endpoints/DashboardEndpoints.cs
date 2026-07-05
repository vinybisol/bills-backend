using Application.Abstractions.Services;
using BillsBackend.Api.Contracts;
using BillsBackend.Api.Domain;
using BillsBackend.Api.Identity;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Endpoints;

internal static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/dashboard/month", GetDashboardMonth);
        group.MapGet("/dashboard/year", GetDashboardYear);
        return group;
    }

    private static async Task<IResult> GetDashboardMonth(
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
        // BillEntries/IncomeEntries to the current owner.
        var billEntries = await db.BillEntries
            .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
            .ToListAsync(ct);

        var incomeEntries = await db.IncomeEntries
            .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
            .ToListAsync(ct);

        // Resolve bill -> category via IgnoreQueryFilters lookups, scoped manually by OwnerId,
        // since a bill/category template referenced by an entry may since have been deactivated.
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

        // Group bill entries by category: plannedMyShare over all entries, actualMyShare over paid
        // entries only. Ordered by plannedMyShare descending; categories with no entries are absent.
        var byCategory = billEntries
            .GroupBy(e => billsById[e.BillId].CategoryId)
            .Select(g =>
            {
                var plannedMyShare = g.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
                var actualMyShare = g
                    .Where(e => e.Paid)
                    .Sum(e => EntryCalculations.MyShare(
                        EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                        e.SplitRatioSnapshot));

                return new DashboardCategoryDto(
                    g.Key, categoriesById[g.Key].Name,
                    plannedMyShare, actualMyShare, actualMyShare - plannedMyShare);
            })
            .OrderByDescending(d => d.PlannedMyShare)
            .ToList();

        var plannedExpense = byCategory.Sum(d => d.PlannedMyShare);
        var actualExpense = byCategory.Sum(d => d.ActualMyShare);

        var plannedIncome = incomeEntries.Sum(e => e.PlannedAmount);
        var actualIncome = incomeEntries
            .Where(e => e.Received)
            .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

        // Receivable (the other person's share) split into pending vs. already-received, plus the full
        // (not myShare) value of already-paid bills — computed inline since this endpoint has no
        // per-entry Receivable DTO like GET /api/entries does.
        var receivablePending = billEntries
            .Where(e => !e.Received)
            .Sum(e => EntryCalculations.Receivable(
                EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot));
        var receivableReceived = billEntries
            .Where(e => e.Received)
            .Sum(e => EntryCalculations.Receivable(
                EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot));
        var paidFull = billEntries
            .Where(e => e.Paid)
            .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

        // saldoPrevistoOtimista: assumes everyone pays what they owe.
        var saldoPrevistoOtimista = plannedIncome - plannedExpense;

        // saldoPrevistoPiorCaso: assumes the pending receivable is never paid back.
        var saldoPrevistoPiorCaso = saldoPrevistoOtimista - receivablePending;

        // saldoRealizado: actual cash — received income plus received reimbursements, minus the full
        // (not myShare) amount actually paid for bills.
        var saldoRealizado = actualIncome + receivableReceived - paidFull;

        var summary = new DashboardSummaryDto(
            plannedExpense, actualExpense,
            plannedIncome, actualIncome,
            saldoPrevistoOtimista, saldoRealizado,
            billEntries.Count(e => e.Paid), billEntries.Count,
            incomeEntries.Count(e => e.Received), incomeEntries.Count,
            receivablePending, receivableReceived, paidFull,
            saldoPrevistoOtimista, saldoPrevistoPiorCaso, saldoRealizado);

        return Results.Ok(new DashboardMonthDto(year.Value, month.Value, summary, byCategory));
    }

    // Returns a year-level dashboard: 12 always-present month summaries, a whole-year per-category
    // breakdown of the owner's share, and grand totals. Mirrors GET /api/dashboard/month's
    // enrichment approach (IgnoreQueryFilters scoped by OwnerId), but fetches the whole year once and
    // aggregates per month and per category from the same in-memory sets.
    private static async Task<IResult> GetDashboardYear(
        int? year,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        if (year is null || year < 2000 || year > 2100)
            return Results.BadRequest("year is required and must be between 2000 and 2100.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        // Fetch the whole year once; the global query filter already scopes BillEntries/IncomeEntries
        // to the current owner.
        var billEntries = await db.BillEntries
            .Where(e => e.RefYear == year.Value)
            .ToListAsync(ct);

        var incomeEntries = await db.IncomeEntries
            .Where(e => e.RefYear == year.Value)
            .ToListAsync(ct);

        // Resolve bill -> category via IgnoreQueryFilters lookups, scoped manually by OwnerId,
        // since a bill/category template referenced by an entry may since have been deactivated.
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

        // Build the 12 always-present month summaries (month 1..12, zeroed when no data).
        var billEntriesByMonth = billEntries.ToLookup(e => e.RefMonth);
        var incomeEntriesByMonth = incomeEntries.ToLookup(e => e.RefMonth);

        var months = Enumerable.Range(1, 12)
            .Select(m =>
            {
                var monthBills = billEntriesByMonth[m];
                var monthIncomes = incomeEntriesByMonth[m];

                var plannedExpense = monthBills.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
                var actualExpense = monthBills
                    .Where(e => e.Paid)
                    .Sum(e => EntryCalculations.MyShare(
                        EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                        e.SplitRatioSnapshot));

                var plannedIncome = monthIncomes.Sum(e => e.PlannedAmount);
                var actualIncome = monthIncomes
                    .Where(e => e.Received)
                    .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

                return new DashboardMonthSummaryDto(
                    m, plannedExpense, actualExpense, plannedIncome, actualIncome,
                    plannedIncome - plannedExpense, actualIncome - actualExpense);
            })
            .ToList();

        // Per-category totals across the whole year; categories with no bill entries are omitted.
        var byCategory = billEntries
            .GroupBy(e => billsById[e.BillId].CategoryId)
            .Select(g =>
            {
                var plannedMyShare = g.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
                var actualMyShare = g
                    .Where(e => e.Paid)
                    .Sum(e => EntryCalculations.MyShare(
                        EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                        e.SplitRatioSnapshot));

                return new DashboardCategoryYearDto(g.Key, categoriesById[g.Key].Name, plannedMyShare, actualMyShare);
            })
            .OrderByDescending(d => d.PlannedMyShare)
            .ToList();

        var totals = new DashboardYearTotalsDto(
            months.Sum(m => m.PlannedExpense), months.Sum(m => m.ActualExpense),
            months.Sum(m => m.PlannedIncome), months.Sum(m => m.ActualIncome),
            months.Sum(m => m.SaldoPrevisto), months.Sum(m => m.SaldoReal));

        return Results.Ok(new DashboardYearDto(year.Value, months, byCategory, totals));
    }
}
