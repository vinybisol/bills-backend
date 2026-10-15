namespace Api.Contracts;

internal sealed record CreateAccessLinkRequest(long PersonId, DateTimeOffset? ExpireAt);