using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Application.Services;
using AutoFixture.Xunit3;
using Domain.Entities;
using Moq;
using TestCommon;

namespace Application.Tests.Services;

[ExcludeFromCodeCoverage]
public sealed class AppUserServiceTest
{
    [Theory]
    [AutoMoqData]
    internal async Task FindByFirebaseUidAsync_UserExists_ReturnsAppUser(
        [Frozen] Mock<IAppUserRepository> repoMock,
        AppUserService sut,
        AppUser appUser,
        string firebaseUid,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(s => s.FindByFirebaseUidAsync(firebaseUid, cancellationToken))
                .ReturnsAsync(appUser);

        // Act
        var result = await sut.FindByFirebaseUidAsync(firebaseUid, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(result);
            Assert.Same(result, appUser);
            repoMock.Verify(s => s.FindByFirebaseUidAsync(firebaseUid, cancellationToken),
                        Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task FindByFirebaseUidAsync_UserDoesNotExist_ReturnsNull(
        [Frozen] Mock<IAppUserRepository> repoMock,
        AppUserService sut,
        string firebaseUid,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(s => s.FindByFirebaseUidAsync(firebaseUid, cancellationToken))
                .ReturnsAsync((AppUser?)null);

        // Act
        var result = await sut.FindByFirebaseUidAsync(firebaseUid, cancellationToken);

        // Assert
        Assert.Null(result);
        repoMock.Verify(s => s.FindByFirebaseUidAsync(firebaseUid, cancellationToken), Times.Once);
    }

    [Theory]
    [AutoMoqData]
    internal async Task AddAsync_SaveSucceeds_ReturnsCreatedProvisioningResult(
        [Frozen] Mock<IAppUserRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        AppUserService sut,
        AppUser appUser,
        CancellationToken cancellationToken)
    {
        // Act
        var result = await sut.AddAsync(appUser, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.WasCreated);
            Assert.Same(appUser, result.User);
            repoMock.Verify(v => v.Add(appUser), Times.Once);
            unitOfWorkMock.Verify(v => v.SaveChangesAsync(cancellationToken), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task AddAsync_UserIsNull_ThrowsArgumentNullException(
        [Frozen] Mock<IAppUserRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        AppUserService sut)
    {
        // Act
        async Task act() => await sut.AddAsync(null!, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(act);
        repoMock.Verify(v => v.Add(It.IsAny<AppUser>()), Times.Never);
        unitOfWorkMock.Verify(v => v.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [AutoMoqData]
    internal async Task AddAsync_UserAlreadyExistsLostRace_ShouldThrowsAndReturnAlreadyPersistentUserProvisioningResult(
        [Frozen] Mock<IAppUserRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        AppUserService sut,
        AppUser appUserWanted,
        AppUser appUserExistent
    )
    {
        // Arrange
        unitOfWorkMock.Setup(s => s.SaveChangesAsync(CancellationToken.None))
                .Throws(new UniqueConstraintViolationException(string.Empty, new Exception()));

        repoMock.Setup(s => s.FindByFirebaseUidAsync(appUserWanted.FirebaseUid, CancellationToken.None))
        .ReturnsAsync(appUserExistent);

        // Act
        var result = await sut.AddAsync(appUserWanted, CancellationToken.None);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.False(result.WasCreated);
            Assert.NotSame(result.User, appUserWanted);

            unitOfWorkMock.Verify(v => v.SaveChangesAsync(CancellationToken.None),
                        Times.Once);

            repoMock.Verify(v => v.FindByFirebaseUidAsync(appUserWanted.FirebaseUid, CancellationToken.None),
                        Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task AddAsync_ThrowsExceptionUserNotExists_ShouldReThrows(
    [Frozen] Mock<IAppUserRepository> repoMock,
    [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
    AppUserService sut,
    AppUser appUserWanted
)
    {
        // Arrange
        unitOfWorkMock.Setup(s => s.SaveChangesAsync(CancellationToken.None))
                .Throws(new UniqueConstraintViolationException(string.Empty, new Exception()));

        // Act
        async Task act() => await sut.AddAsync(appUserWanted, CancellationToken.None);

        //Assert
        await Assert.MultipleAsync(async () =>
        {
            await Assert.ThrowsAsync<UniqueConstraintViolationException>(act);
            unitOfWorkMock.Verify(v => v.SaveChangesAsync(CancellationToken.None),
            Times.Once);

            repoMock.Verify(v => v.FindByFirebaseUidAsync(appUserWanted.FirebaseUid, CancellationToken.None),
            Times.Once);
        });
    }
}