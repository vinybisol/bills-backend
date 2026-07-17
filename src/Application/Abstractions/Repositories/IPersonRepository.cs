using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IPersonRepository
{
    void Add(Person person);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);
    Task<Person?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IEnumerable<PersonDto>> GetAllByNameAsync(IPagedQuery<Person> pagedQuery, CancellationToken cancellationToken);
}