using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Calculations;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

internal sealed class BillService(
    IBillRepository repository,
    IBillEntryRepository entryRepository,
    ICategoryRepository categoryRepository,
    IPersonRepository personRepository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IBillService
{
    internal const string NameField = "name";
    internal const string KindField = "kind";
    internal const string DefaultAmountField = "defaultAmount";
    internal const string SplitRatioField = "splitRatio";
    internal const string PersonIdField = "personId";
    internal const string FromMonthField = "fromMonth";
    internal const string NewAmountField = "newAmount";

    internal const string BillEntity = "Despesa";
    internal const string CategoryEntity = "Categoria";
    internal const string PersonEntity = "Pessoa";

    public async Task<Result<BillDto>> CreateAsync(
        string name, long categoryId, BillKindEnum kind, decimal defaultAmount, decimal splitRatio, long? personId, CancellationToken ct)
    {
        var validation = Validate(name, kind, defaultAmount, splitRatio, personId);
        if (validation is not null)
            return validation;

        var referenceError = await ValidateReferencesAsync(categoryId, personId, ct);
        if (referenceError is not null)
            return referenceError;

        var bill = Bill.Create(currentOwner.Id, name, categoryId, kind, defaultAmount, splitRatio, personId, timeProvider.GetUtcNow());

        repository.Add(bill);
        await unitOfWork.SaveChangesAsync(ct);

        return ToDto(bill);
    }

    public async Task<Result<IEnumerable<BillDto>>> GetAllByNameAsync(CancellationToken ct)
    {
        var pagedQuery = new PagedQueryDto<Bill>(1000, 0, b => b.Name);

        var result = await repository.GetAllByNameAsync(pagedQuery, ct);
        if (result is null)
            return Result.Success(Enumerable.Empty<BillDto>());

        return Result.Success(result);
    }

    public async Task<Result<BillDto>> UpdateAsync(
        long id, string name, long categoryId, BillKindEnum kind, decimal defaultAmount, decimal splitRatio, long? personId, CancellationToken ct)
    {
        var validation = Validate(name, kind, defaultAmount, splitRatio, personId);
        if (validation is not null)
            return validation;

        var bill = await repository.GetByIdAsync(id, ct);
        if (bill is null)
            return Error.NotFound(BillEntity);

        var referenceError = await ValidateReferencesAsync(categoryId, personId, ct);
        if (referenceError is not null)
            return referenceError;

        bill.Update(name, categoryId, kind, defaultAmount, splitRatio, personId);
        await unitOfWork.SaveChangesAsync(ct);

        return ToDto(bill);
    }

    public async Task<Result> DeleteByIdAsync(long id, CancellationToken ct)
    {
        var bill = await repository.GetByIdAsync(id, ct);
        if (bill is null)
            return Error.NotFound(BillEntity);

        bill.Deactivate();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Paid entries are frozen and skipped; entries before the given month are never loaded.
    public async Task<Result<BillRecalculationDto>> RecalculateAsync(
        long billId, int fromYear, int fromMonth, decimal newAmount, CancellationToken ct)
    {
        List<Error> errors = [];
        if (fromMonth is < 1 or > 12)
            errors.Add(new Error(FromMonthField, "FromMonth must be between 1 and 12.", ErrorType.Validation));
        if (newAmount < 0)
            errors.Add(new Error(NewAmountField, "NewAmount must be zero or greater.", ErrorType.Validation));
        if (errors.Count > 0)
            return new ValidationError([.. errors]);

        var bill = await repository.GetByIdAsync(billId, ct);
        if (bill is null)
            return Error.NotFound(BillEntity);

        var entriesInRange = await entryRepository.GetByBillFromMonthAsync(billId, fromYear, fromMonth, ct);

        bill.Recalculate(newAmount);
        var outcome = BillRecalculation.ApplyToEntries(entriesInRange, newAmount);

        await unitOfWork.SaveChangesAsync(ct);

        return new BillRecalculationDto(bill.Id, outcome.UpdatedEntries, outcome.SkippedPaid, bill.DefaultAmount);
    }

    public async Task<Result<BillHistoryDto>> GetHistoryAsync(
        long billId, int? fromYear, int? fromMonth, int? toYear, int? toMonth, CancellationToken ct)
    {
        var header = await repository.GetHistoryHeaderAsync(billId, currentOwner.Id, ct);
        if (header is null)
            return Error.NotFound(BillEntity);

        var entries = await entryRepository.GetByBillAsync(billId, ct);

        var historyItems = BillHistoryCalculations.BuildItems(
            entries.Where(e => EntryCalculations.IsInPeriod(e.RefYear, e.RefMonth, fromYear, fromMonth, toYear, toMonth)));
        var summary = BillHistoryCalculations.Summarize(historyItems);

        var items = historyItems
            .Select(i => new BillHistoryItemDto(
                i.Year, i.Month, i.PlannedAmount, i.ActualAmount, i.Effective, i.MyShare, i.Paid, i.PaidDate,
                i.Variation is null ? null : new BillHistoryVariationDto(i.Variation.Value.Abs, i.Variation.Value.Pct)))
            .ToList();

        return new BillHistoryDto(
            header.BillId, header.Name, header.Category, header.SplitRatio, header.Person,
            new BillHistorySummaryDto(summary.AverageEffective, summary.MinEffective, summary.MaxEffective, summary.TotalPaidMyShare),
            items);
    }

    private async Task<Error?> ValidateReferencesAsync(long categoryId, long? personId, CancellationToken ct)
    {
        if (!await categoryRepository.ExistsByIdAsync(categoryId, ct))
            return Error.NotFound(CategoryEntity);

        if (personId is not null && !await personRepository.ExistsByIdAsync(personId.Value, ct))
            return Error.NotFound(PersonEntity);

        return null;
    }

    private static ValidationError? Validate(string name, BillKindEnum kind, decimal defaultAmount, decimal splitRatio, long? personId)
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new Error(NameField, "Bill name cannot be empty or null.", ErrorType.Validation));

        // The JSON enum converter also accepts integers, so out-of-range values must be rejected here.
        if (!Enum.IsDefined(kind))
            errors.Add(new Error(KindField, "Bill kind must be 'recurring' or 'one_off'.", ErrorType.Validation));

        if (defaultAmount < 0)
            errors.Add(new Error(DefaultAmountField, "Default amount must be zero or greater.", ErrorType.Validation));

        if (splitRatio is < 0m or > 1m)
            errors.Add(new Error(SplitRatioField, "SplitRatio must be between 0 and 1.", ErrorType.Validation));
        else if (splitRatio < 1m && personId is null)
            errors.Add(new Error(PersonIdField, "PersonId is required when SplitRatio is less than 1.", ErrorType.Validation));
        else if (splitRatio == 1m && personId is not null)
            errors.Add(new Error(PersonIdField, "PersonId must be null when SplitRatio is 1.", ErrorType.Validation));

        return errors.Count == 0 ? null : new ValidationError([.. errors]);
    }

    private static BillDto ToDto(Bill bill) =>
        new(bill.Id, bill.Name, bill.CategoryId, bill.Kind, bill.DefaultAmount, bill.SplitRatio, bill.PersonId);
}
