using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

/// <summary>Commands over income entries (lançamentos de receita). Received entries are frozen.</summary>
public interface IIncomeEntryService
{
    Task<Result<IncomeEntryDto>> CreateAsync(long incomeId, int year, int month, decimal? plannedAmount, CancellationToken cancellationToken);
    Task<Result> DeleteByIdAsync(long id, CancellationToken cancellationToken);
    Task<Result<IncomeEntryDto>> UpdateAmountsAsync(long id, decimal? plannedAmount, decimal? actualAmount, CancellationToken cancellationToken);
    Task<Result<IncomeEntryDto>> ReceiveAsync(long id, decimal? actualAmount, DateOnly? receivedDate, CancellationToken cancellationToken);
    Task<Result<IncomeEntryDto>> UnreceiveAsync(long id, CancellationToken cancellationToken);
}
