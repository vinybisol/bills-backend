using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;

namespace Application.Services;

internal sealed class AppUserService(
    IAppUserRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentOwner currentOwner) : IAppUserService
{
    public async Task<Result<AppUserDto>> GetCurrentAsync(CancellationToken ct)
    {
        if (currentOwner.Id <= 0)
            return Error.Unauthorized();

        var user = await repository.FindByIdAsync(currentOwner.Id, ct);
        if (user is null)
            return Error.NotFound("Usuário");

        return new AppUserDto(user.Id, user.Name, user.Email);
    }

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