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

    // ========== AddRangeAsync Tests ==========

    [Theory]
    [AutoMoqData]
    internal async Task AddRangeAsync_WithCategories_AddRangeAndSaveChanges(
        [Frozen] Mock<ICategoryRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        CategoryService sut,
        List<Category> categories,
        CancellationToken cancellationToken)
    {
        // Act
        await sut.AddRangeAsync(categories, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            repoMock.Verify(r => r.AddRange(categories), Times.Once);
            unitOfWorkMock.Verify(u => u.SaveChangesAsync(cancellationToken), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task AddRangeAsync_CancelledToken_ThrowsOperationCanceledException(
        [Frozen] Mock<ICategoryRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        CategoryService sut,
        List<Category> categories)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        // Act
        async Task act() => await sut.AddRangeAsync(categories, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    // ========== UpdateAsync Tests ==========

    [Theory]
    [ClassData(typeof(InvalidStrings))]
    internal async Task UpdateAsync_InvalidStrings_ReturnsFailure(
        string name)
    {
        // Arrange
        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        var sut = fixture.Create<CategoryService>();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await sut.UpdateAsync(1, name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains("cannot be empty ou null", result.Error.Message);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task UpdateAsync_CategoryNotFound_ReturnsNotFoundError(
        [Frozen] Mock<ICategoryRepository> repoMock,
        CategoryService sut,
        long id,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(id, cancellationToken))
            .ReturnsAsync((Category)null);

        // Act
        var result = await sut.UpdateAsync(id, name, cancellationToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.True(result.IsFailure);
            Assert.Contains(nameof(Category), result.Error.Message, StringComparison.InvariantCultureIgnoreCase);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task UpdateAsync_SameName_ReturnsConflictError(
        [Frozen] Mock<ICategoryRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        CategoryService sut,
        Category category,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(category.Id, cancellationToken))
            .ReturnsAsync(category);

        repoMock.Setup(r => r.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(true);

        // Act
        var result = await sut.UpdateAsync(category.Id, name, cancellationToken);

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
        [Frozen] Mock<ICategoryRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        CategoryService sut,
        Category category,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        var nameWithWhitespace = $"  {name}  ";

        repoMock.Setup(r => r.GetByIdAsync(category.Id, cancellationToken))
            .ReturnsAsync(category);

        repoMock.Setup(r => r.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(false);

        // Act
        var result = await sut.UpdateAsync(category.Id, nameWithWhitespace, cancellationToken);

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
        [Frozen] Mock<ICategoryRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        CategoryService sut,
        Category category,
        string name,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(category.Id, cancellationToken))
            .ReturnsAsync(category);

        repoMock.Setup(r => r.ExistsByNameAsync(name, cancellationToken))
            .ReturnsAsync(false);

        // Act
        var result = await sut.UpdateAsync(category.Id, name, cancellationToken);

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
        [Frozen] Mock<ICategoryRepository> repoMock,
        CategoryService sut,
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
        [Frozen] Mock<ICategoryRepository> repoMock,
        CategoryService sut,
        List<CategoryDto> categories,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetAllByNameAsync(
                It.Is<PagedQueryDto<Category>>(pq => pq.Take == 1000 && pq.Skip == 0),
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
                It.IsAny<PagedQueryDto<Category>>(), cancellationToken), Times.Once);
        });
    }

    [Theory]
    [AutoMoqData]
    internal async Task GetAllByNameAsync_EmptyResult_ReturnsEmptyCollection(
        [Frozen] Mock<ICategoryRepository> repoMock,
        CategoryService sut,
        CancellationToken cancellationToken)
    {
        // Arrange
        var emptyList = new List<CategoryDto>();
        repoMock.Setup(r => r.GetAllByNameAsync(
                It.IsAny<PagedQueryDto<Category>>(),
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
        [Frozen] Mock<ICategoryRepository> repoMock,
        CategoryService sut)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        repoMock.Setup(r => r.GetAllByNameAsync(
                It.IsAny<PagedQueryDto<Category>>(),
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
    internal async Task DeleteByIdAsync_CategoryNotFound_ReturnsNotFoundError(
        [Frozen] Mock<ICategoryRepository> repoMock,
        CategoryService sut,
        long id,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(id, cancellationToken))
            .ReturnsAsync((Category)null);

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
    internal async Task DeleteByIdAsync_WithValidId_DeactivatesCategoryAndSaveChanges(
        [Frozen] Mock<ICategoryRepository> repoMock,
        [Frozen] Mock<IUnitOfWork> unitOfWorkMock,
        CategoryService sut,
        Category category,
        CancellationToken cancellationToken)
    {
        // Arrange
        repoMock.Setup(r => r.GetByIdAsync(category.Id, cancellationToken))
            .ReturnsAsync(category);

        // Act
        var result = await sut.DeleteByIdAsync(category.Id, cancellationToken);

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
        [Frozen] Mock<ICategoryRepository> repoMock,
        CategoryService sut,
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