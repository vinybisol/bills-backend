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

internal sealed class IncomeEntryService(
    IIncomeEntryRepository repository,
    IIncomeRepository incomeRepository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IIncomeEntryService
{
    internal const string IncomeIdField = "incomeId";

    internal const string IncomeEntity = "Receita";
    internal const string IncomeEntryEntity = "Lançamento de receita";

    internal static readonly Error Frozen = new(
        "IncomeEntry.Frozen", "Received income entries are frozen; unreceive the entry first.", ErrorType.Conflict);

    public async Task<Result<IncomeEntryDto>> CreateAsync(long incomeId, int year, int month, decimal? plannedAmount, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddPeriodErrors(errors, year, month);
        EntryValidation.AddNonNegativeError(errors, plannedAmount, EntryValidation.PlannedAmountField);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var income = await incomeRepository.GetByIdAsync(incomeId, ct);
        if (income is null)
            return Error.NotFound(IncomeEntity);

        // Recurring templates get their entries from the projection only.
        if (income.Kind != IncomeKindEnum.OneOff)
            return new ValidationError([new Error(IncomeIdField, "Only one_off income templates can be used to create entries.", ErrorType.Validation)]);

        var entry = IncomeEntry.Create(
            currentOwner.Id, income.Id, year, month, plannedAmount ?? income.DefaultAmount, timeProvider.GetUtcNow());

        repository.Add(entry);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return Error.Conflict(IncomeEntryEntity);
        }

        return IncomeEntryDto.From(entry);
    }

    public async Task<Result> DeleteByIdAsync(long id, CancellationToken ct)
    {
        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(IncomeEntryEntity);

        if (entry.Received)
            return Frozen;

        repository.Remove(entry);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IncomeEntryDto>> UpdateAmountsAsync(long id, decimal? plannedAmount, decimal? actualAmount, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddNonNegativeError(errors, plannedAmount, EntryValidation.PlannedAmountField);
        EntryValidation.AddNonNegativeError(errors, actualAmount, EntryValidation.ActualAmountField);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(IncomeEntryEntity);

        if (entry.Received)
            return Frozen;

        entry.UpdateAmounts(plannedAmount, actualAmount);
        await unitOfWork.SaveChangesAsync(ct);

        return IncomeEntryDto.From(entry);
    }

    public async Task<Result<IncomeEntryDto>> ReceiveAsync(long id, decimal? actualAmount, DateOnly? receivedDate, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddNonNegativeError(errors, actualAmount, EntryValidation.ActualAmountField);
        var validation = EntryValidation.ToValidationError(errors);
        if (validation is not null)
            return validation;

        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(IncomeEntryEntity);

        // Receiving again would overwrite the frozen actual amount / received date.
        if (entry.Received)
            return Frozen;

        entry.MarkReceived(EntryCalculations.ResolveEventInstant(receivedDate, timeProvider.GetUtcNow()), actualAmount);
        await unitOfWork.SaveChangesAsync(ct);

        return IncomeEntryDto.From(entry);
    }

    // Idempotent: unreceiving an entry that is not received leaves it unchanged.
    public async Task<Result<IncomeEntryDto>> UnreceiveAsync(long id, CancellationToken ct)
    {
        var entry = await repository.GetByIdAsync(id, ct);
        if (entry is null)
            return Error.NotFound(IncomeEntryEntity);

        entry.Unfreeze();
        await unitOfWork.SaveChangesAsync(ct);

        return IncomeEntryDto.From(entry);
    }
}
