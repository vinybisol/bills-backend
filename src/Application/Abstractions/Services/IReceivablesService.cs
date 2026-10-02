using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

/// <summary>
/// "A receber": the other person's share of split bill entries (split ratio &lt; 1 with a person).
/// Lives inside <c>bill_entry</c> (<c>received</c>/<c>receivedDate</c>) and is independent from <c>paid</c>.
/// </summary>
public interface IReceivablesService
{
    Task<Result<ReceivablesMonthDto>> GetMonthAsync(int? year, int? month, CancellationToken cancellationToken);
    Task<Result<BillEntryDto>> MarkAsync(long entryId, DateOnly? receivedDate, CancellationToken cancellationToken);
    Task<Result<BillEntryDto>> UnmarkAsync(long entryId, CancellationToken cancellationToken);

    /// <summary>All-or-nothing: if any id is unknown, foreign or not a receivable, nothing is marked.</summary>
    Task<Result<MarkBatchResultDto>> MarkBatchAsync(IReadOnlyCollection<long>? entryIds, DateOnly? receivedDate, CancellationToken cancellationToken);

    Task<Result<ReceivablesHistoryDto>> GetHistoryAsync(
        long? personId, int? fromYear, int? fromMonth, int? toYear, int? toMonth, string? status, CancellationToken cancellationToken);
}
