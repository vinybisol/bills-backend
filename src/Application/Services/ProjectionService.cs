using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Calculations;
using Domain.Entities;

namespace Application.Services;

internal sealed class ProjectionService(
    IBillRepository billRepository,
    IBillEntryRepository billEntryRepository,
    IIncomeRepository incomeRepository,
    IIncomeEntryRepository incomeEntryRepository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IProjectionService
{
    internal const string YearField = "year";
    internal const int MinYear = 2000;
    internal const int MaxYear = 2100;
    internal const string ProjectionEntity = "Projeção";

    public async Task<Result<ProjectionDto>> ProjectYearAsync(int year, CancellationToken ct)
    {
        if (year is < MinYear or > MaxYear)
            return new ValidationError([new Error(YearField, $"Year must be between {MinYear} and {MaxYear}.", ErrorType.Validation)]);

        var ownerId = currentOwner.Id;
        var now = timeProvider.GetUtcNow();
        var skipped = 0;

        // Template and entry queries are scoped to the current owner by the global query filter;
        // templates are also restricted to active ones, so soft-deleted moldes are never projected.
        var recurringBills = await billRepository.GetRecurringAsync(ct);
        var existingBillMonths = await billEntryRepository.GetExistingMonthsAsync(year, ct);
        List<BillEntry> billEntries = [];
        foreach (var bill in recurringBills)
        {
            var missingMonths = ProjectionCalculations.MissingMonths(bill.Id, existingBillMonths).ToList();
            skipped += ProjectionCalculations.MonthsPerYear - missingMonths.Count;
            // Snapshot of planned amount / split / person: entries never reference the template values.
            billEntries.AddRange(missingMonths.Select(month =>
                BillEntry.Create(ownerId, bill.Id, year, month, bill.DefaultAmount, bill.SplitRatio, bill.PersonId, now)));
        }

        var recurringIncomes = await incomeRepository.GetRecurringAsync(ct);
        var existingIncomeMonths = await incomeEntryRepository.GetExistingMonthsAsync(year, ct);
        List<IncomeEntry> incomeEntries = [];
        foreach (var income in recurringIncomes)
        {
            var missingMonths = ProjectionCalculations.MissingMonths(income.Id, existingIncomeMonths).ToList();
            skipped += ProjectionCalculations.MonthsPerYear - missingMonths.Count;
            incomeEntries.AddRange(missingMonths.Select(month =>
                IncomeEntry.Create(ownerId, income.Id, year, month, income.DefaultAmount, now)));
        }

        if (billEntries.Count > 0 || incomeEntries.Count > 0)
        {
            billEntryRepository.AddRange(billEntries);
            incomeEntryRepository.AddRange(incomeEntries);

            // A single SaveChanges is atomic; a unique violation means a concurrent projection
            // (or manual entry) inserted the same month in between, so nothing is persisted.
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintViolationException)
            {
                return Error.Conflict(ProjectionEntity);
            }
        }

        return new ProjectionDto(year, billEntries.Count, incomeEntries.Count, skipped);
    }
}
