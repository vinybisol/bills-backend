using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface IPersonService
{
    Task<Result<PersonDto>> CreateAsync(string name, CancellationToken cancellationToken);
    Task<Result<IEnumerable<PersonDto>>> GetAllByNameAsync(CancellationToken cancellationToken);
    Task<Result<PersonDto>> UpdateAsync(long id, string name, CancellationToken cancellationToken);
    Task<Result> DeleteByIdAsync(long id, CancellationToken ct);
}