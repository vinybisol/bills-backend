using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

internal sealed class IncomeService(
    IIncomeRepository repository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IIncomeService
{
    internal const string NameField = "name";
    internal const string KindField = "kind";
    internal const string DefaultAmountField = "defaultAmount";

    public async Task<Result<IncomeDto>> CreateAsync(string name, IncomeKindEnum kind, decimal defaultAmount, CancellationToken ct)
    {
        var validation = Validate(name, kind, defaultAmount);
        if (validation is not null)
            return validation;

        var income = Income.Create(currentOwner.Id, name, kind, defaultAmount, timeProvider.GetUtcNow());

        repository.Add(income);
        await unitOfWork.SaveChangesAsync(ct);

        return ToDto(income);
    }

    public async Task<Result<IEnumerable<IncomeDto>>> GetAllByNameAsync(CancellationToken ct)
    {
        var pagedQuery = new PagedQueryDto<Income>(1000, 0, i => i.Name);

        var result = await repository.GetAllByNameAsync(pagedQuery, ct);
        if (result is null)
            return Result.Success(Enumerable.Empty<IncomeDto>());

        return Result.Success(result);
    }

    public async Task<Result<IncomeDto>> UpdateAsync(long id, string name, IncomeKindEnum kind, decimal defaultAmount, CancellationToken ct)
    {
        var validation = Validate(name, kind, defaultAmount);
        if (validation is not null)
            return validation;

        var income = await repository.GetByIdAsync(id, ct);
        if (income is null)
            return Error.NotFound("Receita");

        income.Update(name, kind, defaultAmount);
        await unitOfWork.SaveChangesAsync(ct);

        return ToDto(income);
    }

    public async Task<Result> DeleteByIdAsync(long id, CancellationToken ct)
    {
        var income = await repository.GetByIdAsync(id, ct);
        if (income is null)
            return Error.NotFound("Receita");

        income.Deactivate();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static ValidationError? Validate(string name, IncomeKindEnum kind, decimal defaultAmount)
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new Error(NameField, "Income name cannot be empty or null.", ErrorType.Validation));

        // The JSON enum converter also accepts integers, so out-of-range values must be rejected here.
        if (!Enum.IsDefined(kind))
            errors.Add(new Error(KindField, "Income kind must be 'recurring' or 'one_off'.", ErrorType.Validation));

        if (defaultAmount < 0)
            errors.Add(new Error(DefaultAmountField, "Default amount must be zero or greater.", ErrorType.Validation));

        return errors.Count == 0 ? null : new ValidationError([.. errors]);
    }

    private static IncomeDto ToDto(Income income) =>
        new(income.Id, income.Name, income.Kind, income.DefaultAmount);
}
