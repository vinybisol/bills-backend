using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

/// <summary>Commands over bill entries (lançamentos de despesa). Paid entries are frozen.</summary>
public interface IBillEntryService
{
    Task<Result<BillEntryDto>> CreateAsync(long billId, int year, int month, decimal? plannedAmount, CancellationToken cancellationToken);
    Task<Result> DeleteByIdAsync(long id, CancellationToken cancellationToken);
    Task<Result<BillEntryDto>> UpdateAmountsAsync(long id, decimal? plannedAmount, decimal? actualAmount, CancellationToken cancellationToken);
    Task<Result<BillEntryDto>> PayAsync(long id, decimal? actualAmount, DateOnly? paidDate, CancellationToken cancellationToken);
    Task<Result<BillEntryDto>> UnpayAsync(long id, CancellationToken cancellationToken);
}
