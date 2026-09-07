namespace Domain.Entities;

public sealed class PersonAccessLink
{
    public long Id { get; private set; }
    public long OwnerId { get; private set; }
    public long PersonId { get; private set; }
    public bool Active { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset RevokeAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Person Person { get; private set; } = null!;

    private PersonAccessLink() { }
    private PersonAccessLink(long ownerId, long personId, string tokenHash, DateTimeOffset createdAt)
    {
        OwnerId = ownerId;
        PersonId = personId;
        Active = true;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
    }

    public static PersonAccessLink Create(long ownerId, long personId, string tokenHash, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new(ownerId, personId, tokenHash.Trim(), createdAt);
    }

    public void Revoke(DateTimeOffset revokeAt)
    {
        Active = false;
        RevokeAt = revokeAt;
    }
}