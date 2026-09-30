using Application.DTOs.Factories;
using Application.DTOs.Services;
using Domain.Abstractions;

namespace Application.Abstractions.Services;

public interface ISharedPagesTokenService
{
    Result<SharedPagesTokenDto> Issue(long personId, DateTimeOffset? expiresAt);
    Result<PesonAccessLinkTokenDto> Validate(string token);
}
