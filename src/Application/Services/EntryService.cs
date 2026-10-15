using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Calculations;

namespace Application.Services;

internal sealed class EntryService(
    IBillEntryRepository billEntryRepository,
    IIncomeEntryRepository incomeEntryRepository,
    ICurrentOwner currentOwner) : IEntryService
{
    public async Task<Result<MonthEntriesDto>> GetMonthAsync(int? year, int? month, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddPeriodErrors(errors, year, month);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var billRows = await billEntryRepository.GetMonthWithNamesAsync(year!.Value, month!.Value, currentOwner.Id, ct);
        var incomeRows = await incomeEntryRepository.GetMonthWithNameAsync(year.Value, month.Value, currentOwner.Id, ct);

        var bills = billRows
            .Select(r =>
            {
                var e = r.Entry;
                var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
                return new BillEntryListItemDto(
                    e.Id, e.BillId, r.Name, r.Category,
                    e.PlannedAmount, e.ActualAmount, e.SplitRatioSnapshot, e.PersonId.HasValue ? r.Person : null,
                    effective,
                    EntryCalculations.MyShare(effective, e.SplitRatioSnapshot),
                    EntryCalculations.Receivable(effective, e.SplitRatioSnapshot),
                    e.Paid, e.PaidDate, e.Received, e.ReceivedDate);
            })
            .OrderBy(d => d.Category)
            .ThenBy(d => d.Name)
            .ToList();

        var incomes = incomeRows
            .Select(r =>
            {
                var e = r.Entry;
                return new IncomeEntryListItemDto(
                    e.Id, e.IncomeId, r.Name,
                    e.PlannedAmount, e.ActualAmount,
                    EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                    e.Received, e.ReceivedDate);
            })
            .ToList();

        var billEntries = billRows.Select(r => r.Entry).ToList();
        var incomeEntries = incomeRows.Select(r => r.Entry).ToList();

        // Pending + received always equals the total split amount owed by other people.
        var receivablePending = EntryAggregations.ReceivablePending(billEntries);
        var receivableReceived = EntryAggregations.ReceivableReceived(billEntries);
        var paidFull = EntryAggregations.PaidFull(billEntries);
        var incomesPlanned = EntryAggregations.PlannedIncome(incomeEntries);
        // Unlike incomesEffective, this does not fall back to planned for entries not yet received.
        var incomesReceived = EntryAggregations.ReceivedIncome(incomeEntries);

        var balance = BalanceCalculations.ComputeMonthBalance(
            incomesPlanned, EntryAggregations.PlannedMyShare(billEntries), receivablePending,
            incomesReceived, receivableReceived, paidFull);

        var totals = new MonthTotalsDto(
            bills.Sum(d => d.PlannedAmount), bills.Sum(d => d.EffectiveAmount), bills.Sum(d => d.MyShare),
            receivablePending, receivableReceived,
            receivablePending, receivableReceived, paidFull,
            incomesPlanned, incomes.Sum(d => d.EffectiveAmount), incomesReceived,
            balance.PlannedOptimistic, balance.Realized,
            balance.PlannedOptimistic, balance.PlannedWorstCase, balance.Realized);

        return new MonthEntriesDto(year.Value, month.Value, bills, incomes, totals);
    }
}
