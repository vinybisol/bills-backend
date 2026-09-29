using Application.Abstractions.Repositories;
using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Data.Contexts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class IncomeRepository(AppDbContext db) : IIncomeRepository
{
    private readonly DbSet<Income> _entity = db.Incomes;

    public void Add(Income income) => _entity.Add(income);

    public async Task<Income?> GetByIdAsync(long id, CancellationToken ct) => await _entity.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IEnumerable<IncomeDto>> GetAllByNameAsync(IPagedQuery<Income> pagedQuery, CancellationToken ct) => await _entity
        .AsNoTracking()
        .OrderBy(pagedQuery.OrderBy)
        .ThenBy(i => i.Id)
        .Skip(pagedQuery.Skip)
        .Take(pagedQuery.Take)
        .Select(i => new IncomeDto(i.Id, i.Name, i.Kind, i.DefaultAmount))
        .ToListAsync(ct);

    public async Task<IReadOnlyList<Income>> GetRecurringAsync(CancellationToken ct) => await _entity
        .AsNoTracking()
        .Where(i => i.Kind == IncomeKindEnum.Recurring)
        .OrderBy(i => i.Id)
        .ToListAsync(ct);
}
