namespace Application.DTOs.Services;

public sealed record PesonAccessLinkTokenDto(long PersonId, Guid TokenId, DateTimeOffset? IssuedAt);