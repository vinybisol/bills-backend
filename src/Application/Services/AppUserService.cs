using System.Runtime.CompilerServices;
using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Domain.Entities;

[assembly: InternalsVisibleTo("Application.Tests")]
namespace Application.Services;

internal sealed class AppUserService(
    IAppUserRepository repository,
    IUnitOfWork unitOfWork) : IAppUserService
{
    public async Task<UserProvisioningResult> AddAsync(AppUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            repository.Add(user);
            await unitOfWork.SaveChangesAsync(ct);
            return new UserProvisioningResult(user, true);
        }
        catch (UniqueConstraintViolationException)
        {
            var existingUser = await repository.FindByFirebaseUidAsync(user.FirebaseUid, ct);
            if (existingUser is not null)
                return new(existingUser, WasCreated: false);

            throw;
        }

    }

    public async Task<AppUser?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken ct) => await repository.FindByFirebaseUidAsync(firebaseUid, ct);
}