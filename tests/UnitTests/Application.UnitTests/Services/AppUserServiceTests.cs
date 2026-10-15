using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class AppUserServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private IAppUserRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ICurrentOwner _currentOwner = null!;
    private AppUserService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IAppUserRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _sut = new AppUserService(_repository, _unitOfWork, _currentOwner);
    }

    private static AppUser NewUser(string uid = "uid-1") => AppUser.Provision(uid, "a@example.com", "Alice", FixedNow);

    [Test]
    public async Task GetCurrentAsync_OwnerExists_ReturnsProfile()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _currentOwner.Id.Returns(42L);
        _repository.FindByIdAsync(42L, cts.Token).Returns(EntityId.With(NewUser(), 42L));

        // Act
        var result = await _sut.GetCurrentAsync(cts.Token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Id, Is.EqualTo(42L));
            Assert.That(result.Value.Name, Is.EqualTo("Alice"));
            Assert.That(result.Value.Email, Is.EqualTo("a@example.com"));
        }
    }

    [Test]
    public async Task GetCurrentAsync_OwnerWithoutEmailOrName_ReturnsEmptyNameAndNullEmail()
    {
        // Arrange
        _currentOwner.Id.Returns(7L);
        _repository.FindByIdAsync(7L, Arg.Any<CancellationToken>())
            .Returns(EntityId.With(AppUser.Provision("uid-7", null, null, FixedNow), 7L));

        // Act
        var result = await _sut.GetCurrentAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Name, Is.EqualTo(string.Empty));
            Assert.That(result.Value.Email, Is.Null);
        }
    }

    [Test]
    public async Task GetCurrentAsync_OwnerNotFound_ReturnsNotFound()
    {
        // Arrange
        _currentOwner.Id.Returns(42L);
        _repository.FindByIdAsync(42L, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        // Act
        var result = await _sut.GetCurrentAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        }
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task GetCurrentAsync_NoOwnerResolved_ReturnsUnauthorizedWithoutQuerying(long ownerId)
    {
        // Arrange
        _currentOwner.Id.Returns(ownerId);

        // Act
        var result = await _sut.GetCurrentAsync(CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Unauthorized));
        await _repository.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Test]
    public async Task FindByFirebaseUidAsync_UserExists_ReturnsUser()
    {
        // Arrange
        var user = NewUser();
        _repository.FindByFirebaseUidAsync("uid-1", Arg.Any<CancellationToken>()).Returns(user);

        // Act
        var result = await _sut.FindByFirebaseUidAsync("uid-1", CancellationToken.None);

        // Assert
        Assert.That(result, Is.SameAs(user));
    }

    [Test]
    public async Task FindByFirebaseUidAsync_UserDoesNotExist_ReturnsNull()
    {
        // Arrange
        _repository.FindByFirebaseUidAsync("uid-1", Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        // Act
        var result = await _sut.FindByFirebaseUidAsync("uid-1", CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AddAsync_SaveSucceeds_ReturnsCreatedResult()
    {
        // Arrange
        var user = NewUser();
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _sut.AddAsync(user, cts.Token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.WasCreated, Is.True);
            Assert.That(result.User, Is.SameAs(user));
        }
        _repository.Received(1).Add(user);
        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }

    [Test]
    public void AddAsync_NullUser_ThrowsArgumentNullExceptionWithoutPersisting()
    {
        // Act / Assert
        Assert.That(async () => await _sut.AddAsync(null!, CancellationToken.None), Throws.ArgumentNullException);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task AddAsync_UniqueViolationAndUserAlreadyPersisted_ReturnsExistingUserNotCreated()
    {
        // Arrange — lost the race: another request provisioned the same uid first
        var wanted = NewUser("uid-race");
        var existing = NewUser("uid-race");
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintViolationException("ux_app_user_firebase_uid", new InvalidOperationException()));
        _repository.FindByFirebaseUidAsync("uid-race", Arg.Any<CancellationToken>()).Returns(existing);

        // Act
        var result = await _sut.AddAsync(wanted, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.WasCreated, Is.False);
            Assert.That(result.User, Is.SameAs(existing));
        }
        await _repository.Received(1).FindByFirebaseUidAsync("uid-race", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddAsync_UniqueViolationAndUserNotFound_Rethrows()
    {
        // Arrange
        var wanted = NewUser("uid-ghost");
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintViolationException("ux", new InvalidOperationException()));
        _repository.FindByFirebaseUidAsync("uid-ghost", Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        // Act / Assert
        await Assert.ThatAsync(
            () => _sut.AddAsync(wanted, CancellationToken.None),
            Throws.TypeOf<UniqueConstraintViolationException>());
        await _repository.Received(1).FindByFirebaseUidAsync("uid-ghost", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddAsync_OtherPersistenceError_PropagatesWithoutLookup()
    {
        // Arrange
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());

        // Act / Assert
        await Assert.ThatAsync(() => _sut.AddAsync(NewUser(), CancellationToken.None), Throws.TypeOf<TimeoutException>());
        await _repository.DidNotReceiveWithAnyArgs().FindByFirebaseUidAsync(default!, default);
    }
}
