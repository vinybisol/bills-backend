namespace Application.DTOs.Factories;

public sealed record class SharedPagesTokenDto(
    Guid TokenId,
    string Token,
    DateTimeOffset? ExpiresAt
);
