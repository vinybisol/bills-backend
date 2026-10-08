using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Factories;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using NSubstitute;
using UnitTestCommon;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class PersonAccessLinksServiceTests
{
    private const long _ownerId = 7L;
    private const long _personId = 10L;
    private static readonly DateTimeOffset _fixedNow = new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _expiresAt = new(2026, 9, 10, 14, 0, 0, TimeSpan.Zero);

    private IPersonRepository _personRepository = null!;
    private IPersonAccessLinksRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ICurrentOwner _currentOwner = null!;
    private ISharedPagesTokenService _sharedPagesTokenService = null!;
    private PersonAccessLinksService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _personRepository = Substitute.For<IPersonRepository>();
        _repository = Substitute.For<IPersonAccessLinksRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _sharedPagesTokenService = Substitute.For<ISharedPagesTokenService>();
        _currentOwner.Id.Returns(_ownerId);
        _sut = new PersonAccessLinksService(
            _personRepository, _repository, _unitOfWork, _currentOwner, _sharedPagesTokenService, new FixedTimeProvider(_fixedNow));
    }

    private void ArrangeExistingPerson() =>
        _personRepository.GetByIdAsync(_personId, Arg.Any<CancellationToken>())
            .Returns(EntityId.With(Person.Create(_ownerId, "Ana", _fixedNow), _personId));

    // --- CreateAsync ---

    [TestCaseSource(typeof(InvalidEntityIds), nameof(InvalidEntityIds.Cases))]
    public async Task CreateAsync_NonPositivePersonId_ReturnsValidationErrorWithoutLookup(long personId)
    {
        // Act
        var result = await _sut.CreateAsync(personId, _expiresAt, CancellationToken.None);

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
        _personRepository.GetByIdAsync(_personId, Arg.Any<CancellationToken>()).Returns((Person?)null);

        // Act
        var result = await _sut.CreateAsync(_personId, _expiresAt, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_TokenAlreadyExpired_ReturnsValidationWithoutPersisting()
    {
        // Arrange
        ArrangeExistingPerson();
        var expiresAt = _fixedNow.AddSeconds(-1);

        // Act
        var result = await _sut.CreateAsync(_personId, expiresAt, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(result.Error.Message, Does.Contain("Token with worng expire time"));
        }
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_ErrorOnIssueToken_ReturnsInvalidOperationWithoutPersisting()
    {
        // Arrange
        ArrangeExistingPerson();
        _sharedPagesTokenService.Issue(_personId, _expiresAt)
            .Returns(Error.InvalidOperation);

        // Act
        var result = await _sut.CreateAsync(_personId, _expiresAt, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Failure));
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_ValidPerson_ReturnsValidToken()
    {
        // Arrange
        ArrangeExistingPerson();
        var token = "tokenFake";
        var sharedPagesTokenDto = new SharedPagesTokenDto(Guid.NewGuid(), token, _expiresAt);
        _sharedPagesTokenService.Issue(_personId, _expiresAt)
            .Returns(sharedPagesTokenDto);

        // Act
        var result = await _sut.CreateAsync(_personId, _expiresAt, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Token, Is.EqualTo(token));
        }
    }
}
