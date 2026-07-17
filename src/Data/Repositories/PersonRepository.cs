using Application.Abstractions.Repositories;
using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Data.Contexts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

internal sealed class PersonRepository(AppDbContext db) : IPersonRepository
{
    private readonly DbSet<Person> _entity = db.Persons;
    public void Add(Person person) => _entity.Add(person);

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct) => await _entity.AnyAsync(f => f.Name == name, ct);

    public async Task<IEnumerable<PersonDto>> GetAllByNameAsync(IPagedQuery<Person> pagedQuery, CancellationToken ct) => await _entity
         .AsNoTracking()
          .OrderBy(pagedQuery.OrderBy)
           .Skip(pagedQuery.Skip)
            .Take(pagedQuery.Take)
             .Select(s => new PersonDto(s.Id, s.Name))
              .ToListAsync(ct);

    public async Task<Person?> GetByIdAsync(long id, CancellationToken ct) => await _entity.FirstOrDefaultAsync(f => f.Id == id, ct);
}