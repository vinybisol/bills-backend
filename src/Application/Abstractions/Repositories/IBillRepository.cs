using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IBillRepository
{
    void Add(Bill bill);
    Task<Bill?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IEnumerable<BillDto>> GetAllByNameAsync(IPagedQuery<Bill> pagedQuery, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the history header of an owner's bill, including deactivated templates (history must
    /// still resolve after a soft delete). Returns <see langword="null"/> when the bill does not exist
    /// or belongs to another owner.
    /// </summary>
    Task<BillHistoryHeaderDto?> GetHistoryHeaderAsync(long billId, long ownerId, CancellationToken cancellationToken);
}
