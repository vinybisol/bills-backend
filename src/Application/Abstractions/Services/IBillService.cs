using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Enums;

namespace Application.Abstractions.Services;

public interface IBillService
{
    Task<Result<BillDto>> CreateAsync(string name, long categoryId, BillKindEnum kind, decimal defaultAmount, decimal splitRatio, long? personId, CancellationToken cancellationToken);
    Task<Result<IEnumerable<BillDto>>> GetAllByNameAsync(CancellationToken cancellationToken);
    Task<Result<BillDto>> UpdateAsync(long id, string name, long categoryId, BillKindEnum kind, decimal defaultAmount, decimal splitRatio, long? personId, CancellationToken cancellationToken);
    Task<Result> DeleteByIdAsync(long id, CancellationToken cancellationToken);
    Task<Result<BillRecalculationDto>> RecalculateAsync(long billId, int fromYear, int fromMonth, decimal newAmount, CancellationToken cancellationToken);
    Task<Result<BillHistoryDto>> GetHistoryAsync(long billId, int? fromYear, int? fromMonth, int? toYear, int? toMonth, CancellationToken cancellationToken);
}
