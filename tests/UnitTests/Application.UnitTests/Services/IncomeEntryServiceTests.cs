using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Application.DTOs.Services;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Domain.Enums;
using NSubstitute;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class IncomeEntryServiceTests
{
    private const long OwnerId = 7L;
    private const long IncomeId = 10L;
    private const long EntryId = 40L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IIncomeEntryRepository _repository = null!;
    private IIncomeRepository _incomeRepository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private IncomeEntryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IIncomeEntryRepository>();
        _incomeRepository = Substitute.For<IIncomeRepository>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new IncomeEntryService(_repository, _incomeRepository, _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private void GivenIncome(IncomeKindEnum kind = IncomeKindEnum.OneOff, decimal defaultAmount = 500m)
    {
        var income = EntityId.With(Income.Create(OwnerId, "Freela", kind, defaultAmount, FixedNow), IncomeId);
        _incomeRepository.GetByIdAsync(IncomeId, Arg.Any<CancellationToken>()).Returns(income);
    }

    private IncomeEntry GivenEntry(bool received = false, decimal planned = 100m, decimal? actual = null)
    {
        var entry = EntityId.With(IncomeEntry.Create(OwnerId, IncomeId, 2026, 3, planned, FixedNow), EntryId);
        if (actual.HasValue)
            entry.UpdateAmounts(null, actual);
        if (received)
            entry.MarkReceived(FixedNow.AddDays(-3), actual);
        _repository.GetByIdAsync(EntryId, Arg.Any<CancellationToken>()).Returns(entry);
        return entry;
    }

    private static void AssertValidationFailure(Result result, params string[] expectedCodes)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.InstanceOf<ValidationError>());
        }
        Assert.That(((ValidationError)result.Error).Errors.Select(e => e.Code), Is.EquivalentTo(expectedCodes));
    }

    private static void AssertFailure(Result result, ErrorType type)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(type));
        }
    }

    private async Task AssertNotSavedAsync() =>
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);

    // --- CreateAsync ---

    [Test]
    public async Task CreateAsync_OneOffIncome_PersistsEntryWithPlannedSnapshot()
    {
        // Arrange
        GivenIncome();
        IncomeEntry? added = null;
        _repository.Add(Arg.Do<IncomeEntry>(e => added = e));

        // Act
        var result = await _sut.CreateAsync(IncomeId, 2026, 4, 250m, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(added.CreatedAt, Is.EqualTo(FixedNow));
            Assert.That(result.Value, Is.EqualTo(new IncomeEntryDto(0, IncomeId, 2026, 4, 250m, null, false, null)));
        }
    }

    [Test]
    public async Task CreateAsync_NullPlannedAmount_UsesTemplateDefaultAmount()
    {
        // Arrange
        GivenIncome(defaultAmount: 500m);

        // Act
        var result = await _sut.CreateAsync(IncomeId, 2026, 4, null, CancellationToken.None);

        // Assert
        Assert.That(result.Value.PlannedAmount, Is.EqualTo(500m));
    }

    [TestCase(2026, 0, 1.0, EntryValidation.MonthField)]
    [TestCase(2026, 13, 1.0, EntryValidation.MonthField)]
    [TestCase(1999, 1, 1.0, EntryValidation.YearField)]
    [TestCase(2101, 1, 1.0, EntryValidation.YearField)]
    [TestCase(2026, 1, -0.01, EntryValidation.PlannedAmountField)]
    public async Task CreateAsync_InvalidInput_ReturnsValidationErrorWithoutTouchingRepositories(int year, int month, decimal planned, string field)
    {
        // Act
        var result = await _sut.CreateAsync(IncomeId, year, month, planned, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, field);
        await _incomeRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task CreateAsync_IncomeNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.CreateAsync(IncomeId, 2026, 4, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        Assert.That(result.Error.Message, Does.StartWith(IncomeEntryService.IncomeEntity));
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task CreateAsync_RecurringIncome_ReturnsValidationErrorOnIncomeId()
    {
        // Arrange
        GivenIncome(kind: IncomeKindEnum.Recurring);

        // Act
        var result = await _sut.CreateAsync(IncomeId, 2026, 4, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, IncomeEntryService.IncomeIdField);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task CreateAsync_DuplicateMonth_ReturnsConflict()
    {
        // Arrange
        GivenIncome();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new UniqueConstraintViolationException("ux", new InvalidOperationException())));

        // Act
        var result = await _sut.CreateAsync(IncomeId, 2026, 4, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.Conflict);
    }

    // --- DeleteByIdAsync ---

    [Test]
    public async Task DeleteByIdAsync_NotReceivedEntry_RemovesAndSaves()
    {
        // Arrange
        var entry = GivenEntry();

        // Act
        var result = await _sut.DeleteByIdAsync(EntryId, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _repository.Received(1).Remove(entry);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteByIdAsync_ReceivedEntry_ReturnsFrozenConflict()
    {
        // Arrange
        GivenEntry(received: true);

        // Act
        var result = await _sut.DeleteByIdAsync(EntryId, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(IncomeEntryService.Frozen));
        _repository.DidNotReceiveWithAnyArgs().Remove(default!);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task DeleteByIdAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.DeleteByIdAsync(EntryId, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        Assert.That(result.Error.Message, Does.StartWith(IncomeEntryService.IncomeEntryEntity));
        await AssertNotSavedAsync();
    }

    // --- UpdateAmountsAsync ---

    [Test]
    public async Task UpdateAmountsAsync_NotReceivedEntry_UpdatesBothAmounts()
    {
        // Arrange
        GivenEntry();

        // Act
        var result = await _sut.UpdateAmountsAsync(EntryId, 120m, 115m, CancellationToken.None);

        // Assert
        Assert.That((result.Value.PlannedAmount, result.Value.ActualAmount), Is.EqualTo((120m, (decimal?)115m)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateAmountsAsync_ReceivedEntry_ReturnsFrozenConflictAndKeepsAmounts()
    {
        // Arrange
        var entry = GivenEntry(received: true, actual: 95m);

        // Act
        var result = await _sut.UpdateAmountsAsync(EntryId, 200m, 199m, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(IncomeEntryService.Frozen));
        Assert.That((entry.PlannedAmount, entry.ActualAmount), Is.EqualTo((100m, (decimal?)95m)));
        await AssertNotSavedAsync();
    }

    [TestCase(-1, null, EntryValidation.PlannedAmountField)]
    [TestCase(null, -1, EntryValidation.ActualAmountField)]
    public async Task UpdateAmountsAsync_NegativeAmount_ReturnsValidationErrorWithoutLoading(int? planned, int? actual, string field)
    {
        // Act
        var result = await _sut.UpdateAmountsAsync(EntryId, planned, actual, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, field);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task UpdateAmountsAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.UpdateAmountsAsync(EntryId, 1m, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
    }

    // --- ReceiveAsync ---

    [Test]
    public async Task ReceiveAsync_WithoutAmountOrDate_ReceivesPlannedNow()
    {
        // Arrange
        GivenEntry(planned: 100m);

        // Act
        var result = await _sut.ReceiveAsync(EntryId, null, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Received, Is.True);
            Assert.That(result.Value.ActualAmount, Is.EqualTo(100m));
            Assert.That(result.Value.ReceivedDate, Is.EqualTo(FixedNow));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveAsync_WithAmountAndDate_RecordsThemAtMidnightUtc()
    {
        // Arrange
        GivenEntry();

        // Act
        var result = await _sut.ReceiveAsync(EntryId, 87.5m, new DateOnly(2026, 3, 10), CancellationToken.None);

        // Assert
        Assert.That((result.Value.ActualAmount, result.Value.ReceivedDate),
            Is.EqualTo(((decimal?)87.5m, (DateTimeOffset?)new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero))));
    }

    [Test]
    public async Task ReceiveAsync_AlreadyReceived_ReturnsFrozenConflictAndKeepsValues()
    {
        // Arrange
        var entry = GivenEntry(received: true, actual: 95m);
        var receivedDate = entry.ReceivedDate;

        // Act
        var result = await _sut.ReceiveAsync(EntryId, 1m, new DateOnly(2026, 1, 1), CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(IncomeEntryService.Frozen));
        Assert.That((entry.ActualAmount, entry.ReceivedDate), Is.EqualTo(((decimal?)95m, receivedDate)));
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task ReceiveAsync_NegativeActualAmount_ReturnsValidationError()
    {
        // Act
        var result = await _sut.ReceiveAsync(EntryId, -1m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, EntryValidation.ActualAmountField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task ReceiveAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.ReceiveAsync(EntryId, null, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        await AssertNotSavedAsync();
    }

    // --- UnreceiveAsync ---

    [Test]
    public async Task UnreceiveAsync_ReceivedEntry_UnfreezesAndKeepsActual()
    {
        // Arrange
        GivenEntry(received: true, actual: 95m);

        // Act
        var result = await _sut.UnreceiveAsync(EntryId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Received, Is.False);
            Assert.That(result.Value.ReceivedDate, Is.Null);
            Assert.That(result.Value.ActualAmount, Is.EqualTo(95m));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnreceiveAsync_NotReceivedEntry_IsIdempotent()
    {
        // Arrange
        GivenEntry();

        // Act
        var result = await _sut.UnreceiveAsync(EntryId, CancellationToken.None);

        // Assert
        Assert.That((result.IsSuccess, result.Value.Received), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task UnreceiveAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.UnreceiveAsync(EntryId, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        await AssertNotSavedAsync();
    }
}
