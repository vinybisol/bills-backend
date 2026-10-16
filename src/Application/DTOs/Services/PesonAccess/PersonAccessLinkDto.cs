namespace Application.DTOs.Services.PesonAccess;

public sealed record PesonAccessLinkDto(long Id, string PersonName, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokeAt);
public sealed record ValidatePesonAccessLinkDto(long OwnerId, long PersonId);
public sealed record CreatePesonAccessLinkDto(string Token);