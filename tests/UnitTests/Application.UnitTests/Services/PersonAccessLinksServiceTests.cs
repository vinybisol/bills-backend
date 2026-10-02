using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Repositories;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using NSubstitute;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class PersonAccessLinksServiceTests
{
    private const long OwnerId = 7L;
    private const long PersonId = 10L;
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);

    private IPersonRepository _personRepository = null!;
    private IPersonAccessLinksRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ICurrentOwner _currentOwner = null!;
    private PersonAccessLinksService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _personRepository = Substitute.For<IPersonRepository>();
        _repository = Substitute.For<IPersonAccessLinksRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _sut = new PersonAccessLinksService(
            _personRepository, _repository, _unitOfWork, _currentOwner, new FixedTimeProvider(FixedNow));
    }

    private void ArrangeExistingPerson() =>
        _personRepository.GetByIdAsync(PersonId, Arg.Any<CancellationToken>())
            .Returns(EntityId.With(Person.Create(OwnerId, "Ana", FixedNow), PersonId));

    /// <summary>Builds a token in the same wire format the service emits: base64url(JSON { PersonId, Token }).</summary>
    private static string EncodeToken(long personId, string? hash) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { PersonId = personId, Token = hash }));

    private static (long PersonId, string Token) DecodeToken(string token)
    {
        using var json = JsonDocument.Parse(Base64Url.DecodeFromChars(token));
        return (json.RootElement.GetProperty("PersonId").GetInt64(), json.RootElement.GetProperty("Token").GetString()!);
    }

    // --- CreateAsync ---

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task CreateAsync_NonPositivePersonId_ReturnsValidationErrorWithoutLookup(long personId)
    {
        // Act
        var result = await _sut.CreateAsync(personId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        }
        await _personRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task CreateAsync_PersonNotFound_ReturnsNotFoundWithoutPersisting()
    {
        // Arrange
        _personRepository.GetByIdAsync(PersonId, Arg.Any<CancellationToken>()).Returns((Person?)null);

        // Act
        var result = await _sut.CreateAsync(PersonId, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_LinkAlreadyExistsForPerson_ReturnsConflictWithoutPersisting()
    {
        // Arrange
        ArrangeExistingPerson();
        _repository.ExistsByPersonIdAsync(PersonId, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _sut.CreateAsync(PersonId, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_ValidPerson_PersistsActiveLinkForCurrentOwner()
    {
        // Arrange
        ArrangeExistingPerson();
        PersonAccessLink? added = null;
        _repository.Add(Arg.Do<PersonAccessLink>(l => added = l));
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _sut.CreateAsync(PersonId, cts.Token);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(added, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added!.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(added.PersonId, Is.EqualTo(PersonId));
            Assert.That(added.Active, Is.True);
            Assert.That(added.CreatedAt, Is.EqualTo(FixedNow));
            Assert.That(added.TokenHash, Does.Match("^[0-9A-F]{64}$"), "SHA-256 hash in upper-case hex");
        }
        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }

    [Test]
    public async Task CreateAsync_ValidPerson_ReturnsDecodableTokenCarryingPersonIdAndStoredHash()
    {
        // Arrange
        ArrangeExistingPerson();
        PersonAccessLink? added = null;
        _repository.Add(Arg.Do<PersonAccessLink>(l => added = l));

        // Act
        var result = await _sut.CreateAsync(PersonId, CancellationToken.None);

        // Assert
        var (decodedPersonId, decodedHash) = DecodeToken(result.Value.Token);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(decodedPersonId, Is.EqualTo(PersonId));
            Assert.That(decodedHash, Is.EqualTo(added!.TokenHash));
            Assert.That(result.Value.Token, Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="), "base64url-safe");
        }
    }

    [Test]
    public async Task CreateAsync_CalledTwice_GeneratesDifferentTokens()
    {
        // Arrange
        ArrangeExistingPerson();

        // Act
        var first = await _sut.CreateAsync(PersonId, CancellationToken.None);
        var second = await _sut.CreateAsync(PersonId, CancellationToken.None);

        // Assert
        Assert.That(first.Value.Token, Is.Not.EqualTo(second.Value.Token));
    }

    // --- RevokeAsync ---

    [Test]
    public async Task RevokeAsync_LinkNotFound_ReturnsNotFoundWithoutSaving()
    {
        // Arrange
        _repository.GetByIdAsync(5L, Arg.Any<CancellationToken>()).Returns((PersonAccessLink?)null);

        // Act
        var result = await _sut.RevokeAsync(5L, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task RevokeAsync_ExistingLink_DeactivatesStampsRevokeDateAndSaves()
    {
        // Arrange
        var link = PersonAccessLink.Create(OwnerId, PersonId, "HASH", FixedNow.AddDays(-1));
        _repository.GetByIdAsync(5L, Arg.Any<CancellationToken>()).Returns(link);

        // Act
        var result = await _sut.RevokeAsync(5L, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(link.Active, Is.False);
            Assert.That(link.RevokeAt, Is.EqualTo(FixedNow));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- ValidateTokenAsync ---

    [TestCase("not a token !!")]
    [TestCase("bm90LWpzb24")] // base64url of "not-json"
    public async Task ValidateTokenAsync_GarbageToken_ReturnsInvalidOperationFailure(string token)
    {
        // Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(Error.InvalidOperation));
        await _repository.DidNotReceiveWithAnyArgs().GetByPersonIdAndHashAsync(default, default!, default);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ValidateTokenAsync_BlankToken_ReturnsValidationErrorOnTokenFieldWithoutLookup(string? token)
    {
        // Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.InstanceOf<ValidationError>());
        var errors = ((ValidationError)result.Error).Errors;
        Assert.That(errors.Select(e => e.Code), Is.EqualTo(new[] { "token" }));
        await _repository.DidNotReceiveWithAnyArgs().GetByPersonIdAndHashAsync(default, default!, default);
    }

    [Test]
    public async Task ValidateTokenAsync_JsonNullPayload_ReturnsInvalidOperationFailure()
    {
        // Arrange
        var token = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("null"));

        // Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(Error.InvalidOperation));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ValidateTokenAsync_PayloadWithBlankHash_ReturnsInvalidOperationWithoutLookup(string? hash)
    {
        // Act
        var result = await _sut.ValidateTokenAsync(EncodeToken(PersonId, hash), CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(Error.InvalidOperation));
        await _repository.DidNotReceiveWithAnyArgs().GetByPersonIdAndHashAsync(default, default!, default);
    }

    [Test]
    public async Task ValidateTokenAsync_LinkNotFound_ReturnsNotFoundFailure()
    {
        // Arrange
        _repository.GetByPersonIdAndHashAsync(PersonId, "HASH", Arg.Any<CancellationToken>())
            .Returns((PersonAccessLink?)null);

        // Act
        var result = await _sut.ValidateTokenAsync(EncodeToken(PersonId, "HASH"), CancellationToken.None);

        // Assert — a real NotFound error (not Error.None, which Result.Failure rejects by throwing)
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _repository.Received(1).GetByPersonIdAndHashAsync(PersonId, "HASH", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ValidateTokenAsync_MatchingLink_ReturnsSuccess()
    {
        // Arrange
        _repository.GetByPersonIdAndHashAsync(PersonId, "HASH", Arg.Any<CancellationToken>())
            .Returns(PersonAccessLink.Create(OwnerId, PersonId, "HASH", FixedNow));

        // Act
        var result = await _sut.ValidateTokenAsync(EncodeToken(PersonId, "HASH"), CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task ValidateTokenAsync_TokenIssuedByCreate_IsAccepted()
    {
        // Arrange — round-trip: whatever Create stores must be what Validate looks up
        ArrangeExistingPerson();
        PersonAccessLink? added = null;
        _repository.Add(Arg.Do<PersonAccessLink>(l => added = l));
        var created = await _sut.CreateAsync(PersonId, CancellationToken.None);
        _repository.GetByPersonIdAndHashAsync(PersonId, added!.TokenHash, Arg.Any<CancellationToken>()).Returns(added);

        // Act
        var result = await _sut.ValidateTokenAsync(created.Value.Token, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
    }
}
