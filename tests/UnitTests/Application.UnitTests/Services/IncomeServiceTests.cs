using Application.Abstractions.Repositories;
using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Domain.Enums;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class IncomeServiceTests
{
    private const long OwnerId = 7L;
    private const long IncomeId = 10L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IIncomeRepository _repository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private IncomeService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IIncomeRepository>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new IncomeService(_repository, _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private static Income ExistingIncome(long id = IncomeId, string name = "Salário") =>
        EntityId.With(Income.Create(OwnerId, name, IncomeKindEnum.Recurring, 5000m, FixedNow), id);

    private static void AssertValidationFailure(Result result, params string[] expectedCodes)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(result.Error, Is.InstanceOf<ValidationError>());
        }
        var errors = ((ValidationError)result.Error).Errors;
        Assert.That(errors.Select(e => e.Code), Is.EquivalentTo(expectedCodes));
        Assert.That(errors.Select(e => e.Type), Is.All.EqualTo(ErrorType.Validation));
    }

    // --- CreateAsync ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task CreateAsync_BlankName_ReturnsValidationErrorWithoutPersisting(string? name)
    {
        // Act
        var result = await _sut.CreateAsync(name!, IncomeKindEnum.Recurring, 1000m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeService.NameField);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [TestCase(-0.01)]
    [TestCase(-1000)]
    public async Task CreateAsync_NegativeDefaultAmount_ReturnsValidationErrorWithoutPersisting(decimal amount)
    {
        // Act
        var result = await _sut.CreateAsync("Salário", IncomeKindEnum.Recurring, amount, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeService.DefaultAmountField);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(99)]
    public async Task CreateAsync_UndefinedKind_ReturnsValidationErrorWithoutPersisting(int kind)
    {
        // Act
        var result = await _sut.CreateAsync("Salário", (IncomeKindEnum)kind, 1000m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeService.KindField);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_AllFieldsInvalid_ReturnsOneValidationErrorPerField()
    {
        // Act
        var result = await _sut.CreateAsync(" ", (IncomeKindEnum)42, -1m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeService.NameField, IncomeService.KindField, IncomeService.DefaultAmountField);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [TestCase(IncomeKindEnum.Recurring, 5000)]
    [TestCase(IncomeKindEnum.OneOff, 0)]
    public async Task CreateAsync_ValidData_AddsIncomeForCurrentOwnerAndSaves(IncomeKindEnum kind, decimal amount)
    {
        // Arrange
        Income? added = null;
        _repository.Add(Arg.Do<Income>(i => added = i));

        // Act
        var result = await _sut.CreateAsync("Salário", kind, amount, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(new IncomeDto(0L, "Salário", kind, amount)));
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(added.Kind, Is.EqualTo(kind));
            Assert.That(added.DefaultAmount, Is.EqualTo(amount));
            Assert.That(added.CreatedAt, Is.EqualTo(FixedNow));
            Assert.That(added.Active, Is.True);
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateAsync_NameWithSurroundingWhitespace_TrimsName()
    {
        // Act
        var result = await _sut.CreateAsync("  Salário  ", IncomeKindEnum.Recurring, 1m, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Name, Is.EqualTo("Salário"));
        _repository.Received(1).Add(Arg.Is<Income>(i => i.Name == "Salário"));
    }

    [Test]
    public async Task CreateAsync_CancellationToken_IsForwardedToUnitOfWork()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.CreateAsync("Salário", IncomeKindEnum.Recurring, 1m, cts.Token);

        // Assert
        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }

    [Test]
    public async Task CreateAsync_SaveCancelled_PropagatesOperationCanceledException()
    {
        // Arrange
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act / Assert
        await Assert.ThatAsync(
            () => _sut.CreateAsync("Salário", IncomeKindEnum.Recurring, 1m, CancellationToken.None),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // --- GetAllByNameAsync ---

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsItems_ReturnsThem()
    {
        // Arrange
        IEnumerable<IncomeDto> incomes =
        [
            new IncomeDto(1L, "Freela", IncomeKindEnum.OneOff, 100m),
            new IncomeDto(2L, "Salário", IncomeKindEnum.Recurring, 5000m),
        ];
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Income>>(), Arg.Any<CancellationToken>()).Returns(incomes);

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(incomes));
        }
    }

    [Test]
    public async Task GetAllByNameAsync_Always_QueriesFirstPageOfOneThousandOrderedByName()
    {
        // Arrange
        IPagedQuery<Income>? query = null;
        _repository.GetAllByNameAsync(Arg.Do<IPagedQuery<Income>>(q => query = q), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<IncomeDto>());

        // Act
        await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Is.Not.Null);
            Assert.That(query!.Take, Is.EqualTo(1000));
            Assert.That(query.Skip, Is.Zero);
            Assert.That(query.OrderBy.Compile()(ExistingIncome(name: "Zé")), Is.EqualTo("Zé"));
        }
    }

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsEmpty_ReturnsEmptySuccess()
    {
        // Arrange
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Income>>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<IncomeDto>());

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsNull_ReturnsEmptySuccess()
    {
        // Arrange
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Income>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IncomeDto>>(null!));

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Empty);
        }
    }

    // --- UpdateAsync ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task UpdateAsync_BlankName_ReturnsValidationErrorWithoutLookup(string? name)
    {
        // Act
        var result = await _sut.UpdateAsync(IncomeId, name!, IncomeKindEnum.Recurring, 1m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeService.NameField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_NegativeDefaultAmount_ReturnsValidationErrorWithoutLookup()
    {
        // Act
        var result = await _sut.UpdateAsync(IncomeId, "Salário", IncomeKindEnum.Recurring, -1m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeService.DefaultAmountField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task UpdateAsync_UndefinedKind_ReturnsValidationErrorWithoutLookup()
    {
        // Act
        var result = await _sut.UpdateAsync(IncomeId, "Salário", (IncomeKindEnum)5, 1m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeService.KindField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task UpdateAsync_IncomeNotFound_ReturnsNotFoundWithoutSaving()
    {
        // Arrange
        _repository.GetByIdAsync(IncomeId, Arg.Any<CancellationToken>()).Returns((Income?)null);

        // Act
        var result = await _sut.UpdateAsync(IncomeId, "Freela", IncomeKindEnum.OneOff, 1m, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_ValidData_UpdatesTrimmedFieldsAndSaves()
    {
        // Arrange
        var income = ExistingIncome();
        _repository.GetByIdAsync(IncomeId, Arg.Any<CancellationToken>()).Returns(income);

        // Act
        var result = await _sut.UpdateAsync(IncomeId, "  Freelance  ", IncomeKindEnum.OneOff, 2500m, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(new IncomeDto(IncomeId, "Freelance", IncomeKindEnum.OneOff, 2500m)));
            Assert.That(income.Name, Is.EqualTo("Freelance"));
            Assert.That(income.Kind, Is.EqualTo(IncomeKindEnum.OneOff));
            Assert.That(income.DefaultAmount, Is.EqualTo(2500m));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateAsync_CancellationToken_IsForwardedToRepositoryAndUnitOfWork()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _repository.GetByIdAsync(IncomeId, cts.Token).Returns(ExistingIncome());

        // Act
        await _sut.UpdateAsync(IncomeId, "Freela", IncomeKindEnum.OneOff, 1m, cts.Token);

        // Assert
        await _repository.Received(1).GetByIdAsync(IncomeId, cts.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }

    // --- DeleteByIdAsync ---

    [Test]
    public async Task DeleteByIdAsync_IncomeNotFound_ReturnsNotFoundWithoutSaving()
    {
        // Arrange
        _repository.GetByIdAsync(IncomeId, Arg.Any<CancellationToken>()).Returns((Income?)null);

        // Act
        var result = await _sut.DeleteByIdAsync(IncomeId, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task DeleteByIdAsync_ExistingIncome_SoftDeletesAndSaves()
    {
        // Arrange
        var income = ExistingIncome();
        _repository.GetByIdAsync(IncomeId, Arg.Any<CancellationToken>()).Returns(income);

        // Act
        var result = await _sut.DeleteByIdAsync(IncomeId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(income.Active, Is.False);
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
