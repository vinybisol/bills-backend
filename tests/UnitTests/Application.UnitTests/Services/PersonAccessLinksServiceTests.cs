using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs;
using Application.DTOs.Factories;
using Application.DTOs.Services.PesonAccess;
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

    [Test]
    public async Task GetAllAsync_Always_RequestsFirst1000OrderedByCreatedAt()
    {
        // Arrange
        var links = new List<PesonAccessLinkDto>
    {
        new(1, 0, _expiresAt, _fixedNow),
        new(2, 0, _expiresAt, _fixedNow)
    };

        PagedQueryDto<PersonAccessLink, DateTimeOffset>? captured = null;

        _repository
            .GetAllAsync(Arg.Do<PagedQueryDto<PersonAccessLink, DateTimeOffset>>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(links);

        // Act
        var result = await _sut.GetAllAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(links));

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Take, Is.EqualTo(1000));
            Assert.That(captured.Skip, Is.Zero);
        }
    }

    [Test]
    public async Task RevokeAsync_PersonNotFound_ReturnsNotFound()
    {
        //Arrange 
        var id = TestContext.CurrentContext.Random.NextLong();

        //Act
        var result = await _sut.RevokeAsync(id, CancellationToken.None);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.TypeOf<Error>());
            Assert.That(result.Error.Code, Is.EqualTo("Error.NotFound"));
        }
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task RevokeAsync_AlreadyRevoked_ReturnsSuccess()
    {
        //Arrange 
        var personAccessLink = PersonAccessLink.Create(1, 2, Guid.NewGuid(), _expiresAt, _fixedNow);
        personAccessLink.Revoke(_fixedNow);

        var id = TestContext.CurrentContext.Random.NextLong();

        _repository.GetByIdAsync(id, CancellationToken.None)
            .Returns(personAccessLink);

        //Act
        var result = await _sut.RevokeAsync(id, CancellationToken.None);

        //Assert
        Assert.That(result.IsSuccess, Is.True);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task RevokeAsync_Revoke_ReturnsSuccess()
    {
        //Arrange 
        var personAccessLink = PersonAccessLink.Create(1, 2, Guid.NewGuid(), _expiresAt, _fixedNow);

        var id = TestContext.CurrentContext.Random.NextLong();

        _repository.GetByIdAsync(id, CancellationToken.None)
            .Returns(personAccessLink);

        //Act
        var result = await _sut.RevokeAsync(id, CancellationToken.None);

        //Assert
        Assert.That(result.IsSuccess, Is.True);
        await _unitOfWork.Received(1).SaveChangesAsync(default);
    }

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task ValidateTokenAsync_InvalidStrings_ReturnsValidationError(string token)
    {
        //Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("Error.Validation"));
            Assert.That(result.Error.Code, Is.EqualTo("Error.Validation"));
            if (result.Error is ValidationError validationError)
                Assert.That(validationError.Errors, Has.One.Matches<Error>(m => m!.Message == "Token cannot be null, empty or whitespaces"));
        }
        await _repository.DidNotReceiveWithAnyArgs().GetByTokenIdAsync(Arg.Any<Guid>(), default);
    }

    [Test]
    public async Task ValidateTokenAsync_TokenIsInvalid_ReturnsError()
    {
        //Arrange 
        var token = TestContext.CurrentContext.Random.NextGuid().ToString();

        _sharedPagesTokenService.Validate(token)
            .Returns(Error.InvalidOperation);

        //Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("Error.InvalidOperation"));
        }
        await _repository.DidNotReceiveWithAnyArgs().GetByTokenIdAsync(Arg.Any<Guid>(), default);
    }

    [Test]
    public async Task ValidateTokenAsync_PesonAccessLinkTokenNotFound_ReturnsError()
    {
        //Arrange 
        var token = TestContext.CurrentContext.Random.NextGuid().ToString();
        var pesonAccessLinkTokenDto = new PesonAccessLinkTokenDto(0, Guid.NewGuid(), _expiresAt);

        _sharedPagesTokenService.Validate(token)
            .Returns(pesonAccessLinkTokenDto);

        //Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("Error.NotFound"));
        }
        await _repository.Received().GetByTokenIdAsync(Arg.Any<Guid>(), default);
    }

    [Test]
    public async Task ValidateTokenAsync_IsRevoked_ReturnsForbidden()
    {
        //Arrange 
        var token = TestContext.CurrentContext.Random.NextGuid().ToString();
        var pesonAccessLinkTokenDto = new PesonAccessLinkTokenDto(0, Guid.NewGuid(), _expiresAt);
        var personAccessLink = PersonAccessLink.Create(1, 2, Guid.NewGuid(), _expiresAt, _fixedNow);
        personAccessLink.Revoke(_fixedNow);

        _sharedPagesTokenService.Validate(token)
            .Returns(pesonAccessLinkTokenDto);

        _repository.GetByTokenIdAsync(pesonAccessLinkTokenDto.TokenId, CancellationToken.None)
            .Returns(personAccessLink);

        //Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("Error.Forbidden"));
        }
        await _repository.Received().GetByTokenIdAsync(Arg.Any<Guid>(), default);
    }

    [Test]
    public async Task ValidateTokenAsync_IsValid_ReturnsValidatePesonAccessLink()
    {
        //Arrange 
        var token = TestContext.CurrentContext.Random.NextGuid().ToString();
        var pesonAccessLinkTokenDto = new PesonAccessLinkTokenDto(0, Guid.NewGuid(), _expiresAt);
        var personAccessLink = PersonAccessLink.Create(1, 2, Guid.NewGuid(), _expiresAt, _fixedNow);
        personAccessLink.Revoke(_fixedNow.AddDays(1));

        _sharedPagesTokenService.Validate(token)
            .Returns(pesonAccessLinkTokenDto);

        _repository.GetByTokenIdAsync(pesonAccessLinkTokenDto.TokenId, CancellationToken.None)
            .Returns(personAccessLink);

        //Act
        var result = await _sut.ValidateTokenAsync(token, CancellationToken.None);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var value = result.Value;
            Assert.That(value, Is.Not.Null);
            Assert.That(value.OwnerId, Is.EqualTo(personAccessLink.OwnerId));
            Assert.That(value.PersonId, Is.EqualTo(personAccessLink.PersonId));
        }
        await _repository.Received().GetByTokenIdAsync(Arg.Any<Guid>(), default);
    }
}
