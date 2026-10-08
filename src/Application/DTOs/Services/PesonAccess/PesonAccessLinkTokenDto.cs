namespace Application.DTOs.Services.PesonAccess;

public sealed record PesonAccessLinkTokenDto(long PersonId, Guid TokenId, DateTimeOffset? ExpiresAt);