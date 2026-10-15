namespace Domain.Entities;

public sealed class PersonAccessLink
{
    public long Id { get; private set; }
    public long OwnerId { get; private set; }
    public long PersonId { get; private set; }
    public Guid TokenId { get; private set; }
    public DateTimeOffset? RevokeAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Person Person { get; private set; } = null!;

    private PersonAccessLink() { }
    private PersonAccessLink(long ownerId, long personId, Guid tokenId, DateTimeOffset? expiresAt, DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        PersonId = personId;
        TokenId = tokenId;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    public static PersonAccessLink Create(long ownerId, long personId, Guid tokenId, DateTimeOffset? expiresAt, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personId);
        if (tokenId == Guid.Empty)
            throw new ArgumentException("TokenId cannot be empty");

        return new(ownerId, personId, tokenId, expiresAt, createdAt);
    }

    public void Revoke(DateTimeOffset revokeAt)
    {
        RevokeAt = revokeAt;
    }
}