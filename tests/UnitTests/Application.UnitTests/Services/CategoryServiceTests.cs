using Application.Abstractions.Repositories;
using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class CategoryServiceTests
{
    private const long OwnerId = 7L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 16, 0, 0, TimeSpan.Zero);

    private ICategoryRepository _repository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private CategoryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ICategoryRepository>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new CategoryService(_repository, _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private static Category ExistingCategory(long id = 20L, string name = "Moradia") =>
        EntityId.With(Category.Create(OwnerId, name, FixedNow), id);

    // --- CreateCategoryAsync ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task CreateCategoryAsync_BlankName_ReturnsValidationErrorWithoutPersisting(string? name)
    {
        // Act
        var result = await _sut.CreateCategoryAsync(name!, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        }
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Test]
    public async Task CreateCategoryAsync_NameAlreadyExists_ReturnsConflictWithoutPersisting()
    {
        // Arrange
        _repository.ExistsByNameAsync("Moradia", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _sut.CreateCategoryAsync("Moradia", CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateCategoryAsync_ValidName_AddsCategoryForCurrentOwnerAndSaves()
    {
        // Arrange
        Category? added = null;
        _repository.Add(Arg.Do<Category>(c => added = c));
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _sut.CreateCategoryAsync("Lazer", cts.Token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Name, Is.EqualTo("Lazer"));
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(added.CreatedAt, Is.EqualTo(FixedNow));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }

    [Test]
    public async Task CreateCategoryAsync_NameWithSurroundingWhitespace_TrimsBeforeCheckingAndSaving()
    {
        // Act
        var result = await _sut.CreateCategoryAsync("  Lazer  ", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Name, Is.EqualTo("Lazer"));
        await _repository.Received(1).ExistsByNameAsync("Lazer", Arg.Any<CancellationToken>());
        _repository.Received(1).Add(Arg.Is<Category>(c => c.Name == "Lazer"));
    }

    [Test]
    public async Task CreateCategoryAsync_RepositoryCancelled_PropagatesOperationCanceledException()
    {
        // Arrange
        _repository.ExistsByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act / Assert
        await Assert.ThatAsync(
            () => _sut.CreateCategoryAsync("Lazer", CancellationToken.None),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // --- AddRangeAsync ---

    [Test]
    public async Task AddRangeAsync_Categories_AddsAllAndSavesOnce()
    {
        // Arrange
        Category[] categories = [Category.Create(OwnerId, "Moradia", FixedNow), Category.Create(OwnerId, "Lazer", FixedNow)];
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.AddRangeAsync(categories, cts.Token);

        // Assert
        _repository.Received(1).AddRange(categories);
        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }

    // --- UpdateAsync ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task UpdateAsync_BlankName_ReturnsValidationErrorWithoutLookup(string? name)
    {
        // Act
        var result = await _sut.UpdateAsync(20L, name!, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task UpdateAsync_CategoryNotFound_ReturnsNotFound()
    {
        // Arrange
        _repository.GetByIdAsync(20L, Arg.Any<CancellationToken>()).Returns((Category?)null);

        // Act
        var result = await _sut.UpdateAsync(20L, "Habitação", CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_NameAlreadyExists_ReturnsConflictAndKeepsName()
    {
        // Arrange
        var category = ExistingCategory(name: "Moradia");
        _repository.GetByIdAsync(20L, Arg.Any<CancellationToken>()).Returns(category);
        _repository.ExistsByNameAsync("Lazer", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _sut.UpdateAsync(20L, "Lazer", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
            Assert.That(category.Name, Is.EqualTo("Moradia"));
        }
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_ValidData_RenamesTrimmedAndSaves()
    {
        // Arrange
        var category = ExistingCategory(id: 20L, name: "Moradia");
        _repository.GetByIdAsync(20L, Arg.Any<CancellationToken>()).Returns(category);

        // Act
        var result = await _sut.UpdateAsync(20L, "  Habitação  ", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(new CategoryDto(20L, "Habitação")));
            Assert.That(category.Name, Is.EqualTo("Habitação"));
        }
        await _repository.Received(1).ExistsByNameAsync("Habitação", Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- GetAllByNameAsync ---

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsItems_ReturnsThem()
    {
        // Arrange
        IEnumerable<CategoryDto> categories = [new CategoryDto(1L, "Lazer"), new CategoryDto(2L, "Moradia")];
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Category>>(), Arg.Any<CancellationToken>()).Returns(categories);

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(categories));
    }

    [Test]
    public async Task GetAllByNameAsync_Always_QueriesFirstPageOfOneThousand()
    {
        // Act
        await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        await _repository.Received(1).GetAllByNameAsync(
            Arg.Is<IPagedQuery<Category>>(q => q.Take == 1000 && q.Skip == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsNull_ReturnsEmptySuccess()
    {
        // Arrange
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Category>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<CategoryDto>>(null!));

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Empty);
        }
    }

    // --- DeleteByIdAsync ---

    [Test]
    public async Task DeleteByIdAsync_CategoryNotFound_ReturnsNotFound()
    {
        // Arrange
        _repository.GetByIdAsync(20L, Arg.Any<CancellationToken>()).Returns((Category?)null);

        // Act
        var result = await _sut.DeleteByIdAsync(20L, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task DeleteByIdAsync_ExistingCategory_SoftDeletesAndSaves()
    {
        // Arrange
        var category = ExistingCategory();
        _repository.GetByIdAsync(20L, Arg.Any<CancellationToken>()).Returns(category);

        // Act
        var result = await _sut.DeleteByIdAsync(20L, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(category.Active, Is.False);
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
