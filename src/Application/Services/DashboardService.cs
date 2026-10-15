using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Calculations;

namespace Application.Services;

internal sealed class DashboardService(
    IBillEntryRepository billEntryRepository,
    IIncomeEntryRepository incomeEntryRepository,
    ICurrentOwner currentOwner) : IDashboardService
{
    public async Task<Result<DashboardMonthDto>> GetMonthAsync(int? year, int? month, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddPeriodErrors(errors, year, month);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var billRows = await billEntryRepository.GetMonthWithNamesAsync(year!.Value, month!.Value, currentOwner.Id, ct);
        var incomeRows = await incomeEntryRepository.GetMonthWithNameAsync(year.Value, month.Value, currentOwner.Id, ct);

        var bills = billRows.Select(r => r.Entry).ToList();
        var incomes = incomeRows.Select(r => r.Entry).ToList();
        var categoryNames = CategoryNames(billRows);

        var byCategory = SummarizeByCategory(billRows)
            .Select(c => new DashboardCategoryDto(
                c.CategoryId, categoryNames[c.CategoryId],
                c.PlannedMyShare, c.ActualMyShare, c.ActualMyShare - c.PlannedMyShare))
            .ToList();

        var plannedExpense = byCategory.Sum(c => c.PlannedMyShare);
        var actualExpense = byCategory.Sum(c => c.ActualMyShare);
        var plannedIncome = EntryAggregations.PlannedIncome(incomes);
        var actualIncome = EntryAggregations.ReceivedIncome(incomes);
        var receivablePending = EntryAggregations.ReceivablePending(bills);
        var receivableReceived = EntryAggregations.ReceivableReceived(bills);
        var paidFull = EntryAggregations.PaidFull(bills);

        var balance = BalanceCalculations.ComputeMonthBalance(
            plannedIncome, plannedExpense, receivablePending,
            actualIncome, receivableReceived, paidFull);

        var summary = new DashboardSummaryDto(
            plannedExpense, actualExpense,
            plannedIncome, actualIncome,
            balance.PlannedOptimistic, balance.Realized,
            bills.Count(e => e.Paid), bills.Count,
            incomes.Count(e => e.Received), incomes.Count,
            receivablePending, receivableReceived, paidFull,
            balance.PlannedOptimistic, balance.PlannedWorstCase, balance.Realized);

        return new DashboardMonthDto(year.Value, month.Value, summary, byCategory);
    }

    public async Task<Result<DashboardYearDto>> GetYearAsync(int? year, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddYearErrors(errors, year);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var billRows = await billEntryRepository.GetYearWithNamesAsync(year!.Value, currentOwner.Id, ct);
        var incomeRows = await incomeEntryRepository.GetYearWithNameAsync(year.Value, currentOwner.Id, ct);

        var categoryNames = CategoryNames(billRows);

        var months = EntryAggregations
            .SummarizeByMonth(billRows.Select(r => r.Entry), incomeRows.Select(r => r.Entry))
            .Select(m => new DashboardMonthSummaryDto(
                m.Month, m.PlannedExpense, m.ActualExpense, m.PlannedIncome, m.ActualIncome,
                m.PlannedBalance, m.ActualBalance))
            .ToList();

        var byCategory = SummarizeByCategory(billRows)
            .Select(c => new DashboardCategoryYearDto(
                c.CategoryId, categoryNames[c.CategoryId], c.PlannedMyShare, c.ActualMyShare))
            .ToList();

        var totals = new DashboardYearTotalsDto(
            months.Sum(m => m.PlannedExpense), months.Sum(m => m.ActualExpense),
            months.Sum(m => m.PlannedIncome), months.Sum(m => m.ActualIncome),
            months.Sum(m => m.SaldoPrevisto), months.Sum(m => m.SaldoReal));

        return new DashboardYearDto(year.Value, months, byCategory, totals);
    }

    private static IReadOnlyList<CategoryShare> SummarizeByCategory(IReadOnlyList<BillEntryWithNamesDto> rows)
    {
        var categoryOf = rows.ToDictionary(r => r.Entry, r => r.CategoryId);
        return EntryAggregations.SummarizeByCategory(rows.Select(r => r.Entry), e => categoryOf[e]);
    }

    private static Dictionary<long, string> CategoryNames(IEnumerable<BillEntryWithNamesDto> rows) =>
        rows.GroupBy(r => r.CategoryId).ToDictionary(g => g.Key, g => g.First().Category);
}
