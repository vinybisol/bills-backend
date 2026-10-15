using Domain.Entities;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class PersonAccessLinkTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RevokedAt = new(2026, 9, 8, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = new(2026, 9, 8, 9, 35, 0, TimeSpan.Zero);

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(
            () => PersonAccessLink.Create(ownerId, 2L, Guid.NewGuid(), ExpiresAt, CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositivePersonId_ThrowsArgumentOutOfRangeException(long personId) =>
        Assert.That(
            () => PersonAccessLink.Create(1L, personId, Guid.NewGuid(), ExpiresAt, CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Create_TokenIdEmpty_ThrowsArgumentException() =>
        Assert.That(
            () => PersonAccessLink.Create(1L, 2L, Guid.Empty, ExpiresAt, CreatedAt),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Create_ValidArgs_ReturnsActiveLinkWithTrimmedHash()
    {
        //Arrange
        var tokenId = Guid.NewGuid();
        // Act
        var link = PersonAccessLink.Create(1L, 2L, tokenId, ExpiresAt, CreatedAt);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(link.OwnerId, Is.EqualTo(1L));
            Assert.That(link.PersonId, Is.EqualTo(2L));
            Assert.That(link.TokenId, Is.EqualTo(tokenId));
            Assert.That(link.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(link.RevokeAt, Is.Null);
            Assert.That(link.ExpiresAt, Is.EqualTo(ExpiresAt));
        }
    }

    [Test]
    public void Revoke_ActiveLink_DeactivatesAndStampsRevokeDate()
    {
        // Arrange
        var link = PersonAccessLink.Create(1L, 2L, Guid.NewGuid(), ExpiresAt, CreatedAt);

        // Act
        link.Revoke(RevokedAt);

        // Assert
        Assert.That(link.RevokeAt, Is.EqualTo(RevokedAt));

    }
}
