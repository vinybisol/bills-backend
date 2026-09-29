using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IIncomeRepository
{
    void Add(Income income);
    Task<Income?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IEnumerable<IncomeDto>> GetAllByNameAsync(IPagedQuery<Income> pagedQuery, CancellationToken cancellationToken);
}
