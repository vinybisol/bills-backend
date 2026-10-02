using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Calculations;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

internal sealed class BillEntryService(
    IBillEntryRepository repository,
    IBillRepository billRepository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IBillEntryService
{
    internal const string BillIdField = "billId";

    internal const string BillEntity = "Despesa";
    internal const string BillEntryEntity = "Lançamento de despesa";

    internal static readonly Error Frozen = new(
        "BillEntry.Frozen", "Paid bill entries are frozen; unpay the entry first.", ErrorType.Conflict);

    public async Task<Result<BillEntryDto>> CreateAsync(long billId, int year, int month, decimal? plannedAmount, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddPeriodErrors(errors, year, month);
        EntryValidation.AddNonNegativeError(errors, plannedAmount, EntryValidation.PlannedAmountField);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var bill = await billRepository.GetByIdAsync(billId, ct);
        if (bill is null)
            return Error.NotFound(BillEntity);

        // Recurring templates get their entries from the projection only.
        if (bill.Kind != BillKindEnum.OneOff)
            return new ValidationError([new Error(BillIdField, "Only one_off bill templates can be used to create entries.", ErrorType.Validation)]);

        // Snapshot of planned amount / split / person: the entry never references the template values.
        var entry = BillEntry.Create(
            currentOwner.Id, bill.Id, year, month, plannedAmount ?? bill.DefaultAmount,
            bill.SplitRatio, bill.PersonId, timeProvider.GetUtcNow());

        repository.Add(entry);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return Error.Conflict(BillEntryEntity);
        }

        return BillEntryDto.From(entry);
    }

    public async Task<Result> DeleteByIdAsync(long id, CancellationToken ct)
    {
        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(BillEntryEntity);

        if (entry.Paid)
            return Frozen;

        repository.Remove(entry);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<BillEntryDto>> UpdateAmountsAsync(long id, decimal? plannedAmount, decimal? actualAmount, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddNonNegativeError(errors, plannedAmount, EntryValidation.PlannedAmountField);
        EntryValidation.AddNonNegativeError(errors, actualAmount, EntryValidation.ActualAmountField);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(BillEntryEntity);

        if (entry.Paid)
            return Frozen;

        entry.UpdateAmounts(plannedAmount, actualAmount);
        await unitOfWork.SaveChangesAsync(ct);

        return BillEntryDto.From(entry);
    }

    public async Task<Result<BillEntryDto>> PayAsync(long id, decimal? actualAmount, DateOnly? paidDate, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddNonNegativeError(errors, actualAmount, EntryValidation.ActualAmountField);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(BillEntryEntity);

        // Paying again would overwrite the frozen actual amount / paid date.
        if (entry.Paid)
            return Frozen;

        entry.MarkPaid(EntryCalculations.ResolveEventInstant(paidDate, timeProvider.GetUtcNow()), actualAmount);
        await unitOfWork.SaveChangesAsync(ct);

        return BillEntryDto.From(entry);
    }

    // Idempotent; only touches paid/paidDate — received (person paid me back) is independent.
    public async Task<Result<BillEntryDto>> UnpayAsync(long id, CancellationToken ct)
    {
        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(BillEntryEntity);

        entry.Unfreeze();
        await unitOfWork.SaveChangesAsync(ct);

        return BillEntryDto.From(entry);
    }
}
