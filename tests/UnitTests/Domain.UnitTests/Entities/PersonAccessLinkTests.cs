using Domain.Entities;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Entities;

[TestFixture]
public sealed class PersonAccessLinkTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RevokedAt = new(2026, 9, 8, 9, 30, 0, TimeSpan.Zero);

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositiveOwnerId_ThrowsArgumentOutOfRangeException(long ownerId) =>
        Assert.That(
            () => PersonAccessLink.Create(ownerId, 2L, "HASH", CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Create_NonPositivePersonId_ThrowsArgumentOutOfRangeException(long personId) =>
        Assert.That(
            () => PersonAccessLink.Create(1L, personId, "HASH", CreatedAt),
            Throws.InstanceOf<ArgumentOutOfRangeException>());

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void Create_BlankTokenHash_ThrowsArgumentException(string? tokenHash) =>
        Assert.That(
            () => PersonAccessLink.Create(1L, 2L, tokenHash!, CreatedAt),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Create_ValidArgs_ReturnsActiveLinkWithTrimmedHash()
    {
        // Act
        var link = PersonAccessLink.Create(1L, 2L, "  ABC123  ", CreatedAt);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(link.OwnerId, Is.EqualTo(1L));
            Assert.That(link.PersonId, Is.EqualTo(2L));
            Assert.That(link.TokenHash, Is.EqualTo("ABC123"));
            Assert.That(link.Active, Is.True);
            Assert.That(link.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(link.RevokeAt, Is.EqualTo(default(DateTimeOffset)));
        }
    }

    [Test]
    public void Revoke_ActiveLink_DeactivatesAndStampsRevokeDate()
    {
        // Arrange
        var link = PersonAccessLink.Create(1L, 2L, "HASH", CreatedAt);

        // Act
        link.Revoke(RevokedAt);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(link.Active, Is.False);
            Assert.That(link.RevokeAt, Is.EqualTo(RevokedAt));
            Assert.That(link.TokenHash, Is.EqualTo("HASH"));
        }
    }
}
