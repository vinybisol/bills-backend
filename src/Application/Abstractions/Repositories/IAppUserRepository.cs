using Domain.Entities;

namespace Application.Abstractions.Repositories;

public interface IAppUserRepository
{
    void Add(AppUser user);
    Task<AppUser?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken cancellationToken);
}