using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Enums;

namespace Application.Abstractions.Services;

public interface IIncomeService
{
    Task<Result<IncomeDto>> CreateAsync(string name, IncomeKindEnum kind, decimal defaultAmount, CancellationToken cancellationToken);
    Task<Result<IEnumerable<IncomeDto>>> GetAllByNameAsync(CancellationToken cancellationToken);
    Task<Result<IncomeDto>> UpdateAsync(long id, string name, IncomeKindEnum kind, decimal defaultAmount, CancellationToken cancellationToken);
    Task<Result> DeleteByIdAsync(long id, CancellationToken cancellationToken);
}
