using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Repositories;
using Application.DTOs;
using Application.DTOs.Services;
using Application.Services;
using AutoFixture;
using AutoFixture.AutoMoq;
using AutoFixture.Xunit3;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Moq;
using TestCommon;
using TestCommon.TestData;

namespace Application.Tests.Services;

[ExcludeFromCodeCoverage]
public sealed class PersonServiceTest
{
    [Theory]
    [ClassData(typeof(InvalidStrings))]
    internal async Task CreatePersonAsync_InvalidStrings_ReturnsFailure(
        string name)
    {
        // Arrange
        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        var sut = fixture.Create<PersonService>();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await sut.CreateAsync(name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("cannot be empty ou null", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreatePersonAsync_SameName_ReturnsFailure(
            [Frozen] Mock<IPersonRepository> repoMock,
            PersonService sut,
            string name,
            CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(s => s.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(true);

        // Act
        var result = await sut.CreateAsync(name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("that name already exists", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreatePersonAsync_CancelledToken_ThrowsOperationCanceledException(
       [Frozen] Mock<IPersonRepository> repoMock,
       PersonService sut,
       string name)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        repoMock.Setup(s => s.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        async Task act() => await sut.CreateAsync(name, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreatePersonAsync_NameWithWhitespace_TrimsNameBeforeSaving(
    [Frozen] Mock<IPersonRepository> repoMock,
    [Frozen] Mock<ICurrentOwner> currentOwnerMock,
    PersonService sut,
    string name,
    int currentOwner,
    CancellationToken cancellationToken)
    {
        // Arrange
        var nameWithWhitespace = $"  {name}  ";

        repoMock.Setup(s => s.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(false);

        currentOwnerMock.SetupGet(s => s.Id).Returns(currentOwner);

        // Act
        var result = await sut.CreateAsync(nameWithWhitespace, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(name, result.Value.Name);

            repoMock.Verify(s => s.ExistsByNameAsync(name, cancellationToken), Times.Once);
            repoMock.Verify(s => s.Add(It.Is<Person>(c => c.Name == name)), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreatePersonAsync_WithName_ReturnsSuccess(
        [Frozen] Mock<IPersonRepository> repoMock,
        [Frozen] Mock<ICurrentOwner> currentOwnerMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        PersonService sut,
        string name,
        int currentOwner,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(s => s.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(false);

        currentOwnerMock.SetupGet(s => s.Id).Returns(currentOwner);

        // Act
        var result = await sut.CreateAsync(name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            var Person = result.Value;
            Assert.True(result.IsSuccess);
            Assert.Equal(Person.Name, name);

            repoMock.Verify(s => s.Add(It.Is<Person>(c => c.OwnerId == currentOwner)),
            Times.Once);

            unitOfWorkMock.Verify(v => v.SaveChangesAsync(cancellationToken),
            Times.Once);
        });
    }

    // ========== UpdateAsync Tests ==========

    [Theory]
    [ClassData(typeof(InvalidStrings))]
    internal async Task UpdateAsync_InvalidStrings_ReturnsFailure(
        string name)
    {
        // Arrange
        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        var sut = fixture.Create<PersonService>();
        var id = fixture.Create<int>();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await sut.UpdateAsync(id, name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("cannot be empty ou null", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task UpdateAsync_PersonNotFound_ReturnsNotFoundError(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut,
        long id,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(id, cancellationToken))
            .ReturnsAsync((Person)null!);

        // Act
        var result = await sut.UpdateAsync(id, name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains(nameof(Person), result.Error.Message, StringComparison.InvariantCultureIgnoreCase);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task UpdateAsync_SameName_ReturnsConflictError(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut,
        Person Person,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(Person.Id, cancellationToken))
            .ReturnsAsync(Person);

        repoMock.Setup(r => r.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(true);

        // Act
        var result = await sut.UpdateAsync(Person.Id, name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("that name already exists", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task UpdateAsync_NameWithWhitespace_TrimsNameBeforeUpdating(
        [Frozen] Mock<IPersonRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        PersonService sut,
        Person Person,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        var nameWithWhitespace = $"  {name}  ";

        repoMock.Setup(r => r.GetByIdAsync(Person.Id, cancellationToken))
            .ReturnsAsync(Person);

        repoMock.Setup(r => r.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(false);

        // Act
        var result = await sut.UpdateAsync(Person.Id, nameWithWhitespace, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(name, result.Value.Name);
            repoMock.Verify(r => r.ExistsByNameAsync(name, cancellationToken), Times.Once);
            unitOfWorkMock.Verify(u => u.SaveChangesAsync(cancellationToken), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task UpdateAsync_WithValidData_ReturnsSuccess(
        [Frozen] Mock<IPersonRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        PersonService sut,
        Person Person,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(Person.Id, cancellationToken))
            .ReturnsAsync(Person);

        repoMock.Setup(r => r.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(false);

        // Act
        var result = await sut.UpdateAsync(Person.Id, name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(name, result.Value.Name);
            unitOfWorkMock.Verify(u => u.SaveChangesAsync(cancellationToken), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task UpdateAsync_CancelledToken_ThrowsOperationCanceledException(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut,
        long id,
        string name)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        async Task act() => await sut.UpdateAsync(id, name, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    // ========== GetAllByNameAsync Tests ==========

    [Theory]
    [AutoMoqData]
    internal async Task GetAllByNameAsync_WithCategories_ReturnsSuccess(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut,
        List<PersonDto> categories,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetAllByNameAsync(
                It.Is<PagedQueryDto<Person>>(pq => pq.Take == 1000 && pq.Skip == 0),
                cancellationToken))
            .ReturnsAsync(categories);

        // Act
        var result = await sut.GetAllByNameAsync(cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(categories.Count, result.Value.Count());
            repoMock.Verify(r => r.GetAllByNameAsync(
                It.IsAny<PagedQueryDto<Person>>(), cancellationToken), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task GetAllByNameAsync_EmptyResult_ReturnsEmptyCollection(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut,
        CancellationToken cancellationToken)
    {
        // Arrange
        var emptyList = new List<PersonDto>();
        repoMock.Setup(r => r.GetAllByNameAsync(
                It.IsAny<PagedQueryDto<Person>>(),
                cancellationToken))
            .ReturnsAsync(emptyList);

        // Act
        var result = await sut.GetAllByNameAsync(cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task GetAllByNameAsync_CancelledToken_ThrowsOperationCanceledException(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        repoMock.Setup(r => r.GetAllByNameAsync(
                It.IsAny<PagedQueryDto<Person>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        async Task act() => await sut.GetAllByNameAsync(cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    // ========== DeleteByIdAsync Tests ==========

    [Theory]
    [AutoMoqData]
    internal async Task DeleteByIdAsync_PersonNotFound_ReturnsNotFoundError(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut,
        long id,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(id, cancellationToken))
            .ReturnsAsync((Person)null!);

        // Act
        var result = await sut.DeleteByIdAsync(id, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("Categoria", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task DeleteByIdAsync_WithValidId_DeactivatesPersonAndSaveChanges(
        [Frozen] Mock<IPersonRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        PersonService sut,
        Person Person,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(Person.Id, cancellationToken))
            .ReturnsAsync(Person);

        // Act
        var result = await sut.DeleteByIdAsync(Person.Id, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsSuccess);
            unitOfWorkMock.Verify(u => u.SaveChangesAsync(cancellationToken), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task DeleteByIdAsync_CancelledToken_ThrowsOperationCanceledException(
        [Frozen] Mock<IPersonRepository> repoMock,
        PersonService sut,
        long id)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        async Task act() => await sut.DeleteByIdAsync(id, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }
}