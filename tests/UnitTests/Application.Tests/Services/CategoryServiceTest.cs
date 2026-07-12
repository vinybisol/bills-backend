using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Repositories;
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
public sealed class CategoryServiceTest
{
    [Theory]
    [ClassData(typeof(InvalidStrings))]
    internal async Task CreateCategoryAsync_InvalidStrings_ReturnsFailure(
        string name)
    {
        // Arrange
        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        var sut = fixture.Create<CategoryService>();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await sut.CreateCategoryAsync(name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("cannot be empty ou null", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreateCategoryAsync_SameName_ReturnsFailure(
            [Frozen] Mock<ICategoryRepository> repoMock,
            CategoryService sut,
            string name,
            CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(s => s.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(true);

        // Act
        var result = await sut.CreateCategoryAsync(name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("that name already exists", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreateCategoryAsync_CancelledToken_ThrowsOperationCanceledException(
       [Frozen] Mock<ICategoryRepository> repoMock,
       CategoryService sut,
       string name)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        repoMock.Setup(s => s.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        async Task act() => await sut.CreateCategoryAsync(name, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreateCategoryAsync_NameWithWhitespace_TrimsNameBeforeSaving(
    [Frozen] Mock<ICategoryRepository> repoMock,
    [Frozen] Mock<ICurrentOwner> currentOwnerMock,
    [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
    CategoryService sut,
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
        var result = await sut.CreateCategoryAsync(nameWithWhitespace, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(name, result.Value.Name);

            repoMock.Verify(s => s.ExistsByNameAsync(name, cancellationToken), Times.Once);
            repoMock.Verify(s => s.Add(It.Is<Category>(c => c.Name == name)), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task CreateCategoryAsync_WithName_ReturnsSuccess(
        [Frozen] Mock<ICategoryRepository> repoMock,
        [Frozen] Mock<ICurrentOwner> currentOwnerMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        CategoryService sut,
        string name,
        int currentOwner,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(s => s.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(false);

        currentOwnerMock.SetupGet(s => s.Id).Returns(currentOwner);

        // Act
        var result = await sut.CreateCategoryAsync(name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            var category = result.Value;
            Assert.True(result.IsSuccess);
            Assert.Equal(category.Name, name);

            repoMock.Verify(s => s.Add(It.Is<Category>(c => c.OwnerId == currentOwner)),
            Times.Once);

            unitOfWorkMock.Verify(v => v.SaveChangesAsync(cancellationToken),
            Times.Once);
        });
    }
}