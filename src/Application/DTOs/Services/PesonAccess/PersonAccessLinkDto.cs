namespace Application.DTOs.Services.PesonAccess;

public sealed record PesonAccessLinkDto(long Id, long PersonId, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokeAt);
public sealed record CreatePesonAccessLinkDto(long Id, string Token);