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
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class ProjectionServiceTests
{
    private const long OwnerId = 7L;
    private const long BillId = 10L;
    private const long OtherBillId = 11L;
    private const long IncomeId = 40L;
    private const long CategoryId = 20L;
    private const long PersonId = 30L;
    private const int Year = 2026;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IBillRepository _billRepository = null!;
    private IBillEntryRepository _billEntryRepository = null!;
    private IIncomeRepository _incomeRepository = null!;
    private IIncomeEntryRepository _incomeEntryRepository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ProjectionService _sut = null!;

    private List<BillEntry> _addedBillEntries = null!;
    private List<IncomeEntry> _addedIncomeEntries = null!;

    [SetUp]
    public void SetUp()
    {
        _billRepository = Substitute.For<IBillRepository>();
        _billRepository.GetRecurringAsync(Arg.Any<CancellationToken>()).Returns([]);
        _incomeRepository = Substitute.For<IIncomeRepository>();
        _incomeRepository.GetRecurringAsync(Arg.Any<CancellationToken>()).Returns([]);

        _addedBillEntries = [];
        _billEntryRepository = Substitute.For<IBillEntryRepository>();
        _billEntryRepository.GetExistingMonthsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<(long, int)>());
        _billEntryRepository.AddRange(Arg.Do<IEnumerable<BillEntry>>(e => _addedBillEntries.AddRange(e)));

        _addedIncomeEntries = [];
        _incomeEntryRepository = Substitute.For<IIncomeEntryRepository>();
        _incomeEntryRepository.GetExistingMonthsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<(long, int)>());
        _incomeEntryRepository.AddRange(Arg.Do<IEnumerable<IncomeEntry>>(e => _addedIncomeEntries.AddRange(e)));

        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new ProjectionService(
            _billRepository, _billEntryRepository, _incomeRepository, _incomeEntryRepository,
            _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private static Bill RecurringBill(long id = BillId, decimal defaultAmount = 1000m, decimal splitRatio = 1m, long? personId = null) =>
        EntityId.With(Bill.Create(OwnerId, $"Bill {id}", CategoryId, BillKindEnum.Recurring, defaultAmount, splitRatio, personId, FixedNow), id);

    private static Income RecurringIncome(long id = IncomeId, decimal defaultAmount = 5000m) =>
        EntityId.With(Income.Create(OwnerId, $"Income {id}", IncomeKindEnum.Recurring, defaultAmount, FixedNow), id);

    private void GivenBills(params Bill[] bills) =>
        _billRepository.GetRecurringAsync(Arg.Any<CancellationToken>()).Returns(bills);

    private void GivenIncomes(params Income[] incomes) =>
        _incomeRepository.GetRecurringAsync(Arg.Any<CancellationToken>()).Returns(incomes);

    private void GivenExistingBillMonths(params (long, int)[] existing) =>
        _billEntryRepository.GetExistingMonthsAsync(Year, Arg.Any<CancellationToken>()).Returns(existing.ToHashSet());

    private void GivenExistingIncomeMonths(params (long, int)[] existing) =>
        _incomeEntryRepository.GetExistingMonthsAsync(Year, Arg.Any<CancellationToken>()).Returns(existing.ToHashSet());

    private static readonly int[] AllMonths = [.. Enumerable.Range(1, 12)];

    // --- Validation ---

    [TestCase(1999)]
    [TestCase(2101)]
    [TestCase(0)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public async Task ProjectYearAsync_YearOutOfRange_ReturnsValidationErrorWithoutTouchingRepositories(int year)
    {
        // Act
        var result = await _sut.ProjectYearAsync(year, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.InstanceOf<ValidationError>());
            Assert.That(((ValidationError)result.Error).Errors.Select(e => e.Code), Is.EqualTo(new[] { ProjectionService.YearField }));
            Assert.That(((ValidationError)result.Error).Errors.Select(e => e.Type), Is.All.EqualTo(ErrorType.Validation));
        }
        await _billRepository.DidNotReceiveWithAnyArgs().GetRecurringAsync(default);
        await _incomeRepository.DidNotReceiveWithAnyArgs().GetRecurringAsync(default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [TestCase(2000)]
    [TestCase(2100)]
    public async Task ProjectYearAsync_YearAtBoundary_Succeeds(int year)
    {
        // Arrange
        GivenBills(RecurringBill());

        // Act
        var result = await _sut.ProjectYearAsync(year, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(new ProjectionDto(year, 12, 0, 0)));
        Assert.That(_addedBillEntries.Select(e => e.RefYear), Is.All.EqualTo(year));
    }

    // --- Generation ---

    [Test]
    public async Task ProjectYearAsync_NoTemplates_ReturnsZeroCountsAndDoesNotSave()
    {
        // Act
        var result = await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectionDto(Year, 0, 0, 0)));
        _billEntryRepository.DidNotReceiveWithAnyArgs().AddRange(default!);
        _incomeEntryRepository.DidNotReceiveWithAnyArgs().AddRange(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task ProjectYearAsync_RecurringTemplatesWithoutEntries_CreatesTwelveEntriesPerTemplateAndSavesOnce()
    {
        // Arrange
        GivenBills(RecurringBill(BillId), RecurringBill(OtherBillId));
        GivenIncomes(RecurringIncome());

        // Act
        var result = await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectionDto(Year, 24, 12, 0)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_addedBillEntries.Where(e => e.BillId == BillId).Select(e => e.RefMonth), Is.EqualTo(AllMonths));
            Assert.That(_addedBillEntries.Where(e => e.BillId == OtherBillId).Select(e => e.RefMonth), Is.EqualTo(AllMonths));
            Assert.That(_addedIncomeEntries.Select(e => e.RefMonth), Is.EqualTo(AllMonths));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProjectYearAsync_SharedBill_SnapshotsPlannedAmountSplitAndPerson()
    {
        // Arrange
        GivenBills(RecurringBill(defaultAmount: 250.50m, splitRatio: 0.5m, personId: PersonId));

        // Act
        await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(_addedBillEntries, Has.Count.EqualTo(12));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_addedBillEntries.Select(e => e.OwnerId), Is.All.EqualTo(OwnerId));
            Assert.That(_addedBillEntries.Select(e => e.BillId), Is.All.EqualTo(BillId));
            Assert.That(_addedBillEntries.Select(e => e.RefYear), Is.All.EqualTo(Year));
            Assert.That(_addedBillEntries.Select(e => e.PlannedAmount), Is.All.EqualTo(250.50m));
            Assert.That(_addedBillEntries.Select(e => e.SplitRatioSnapshot), Is.All.EqualTo(0.5m));
            Assert.That(_addedBillEntries.Select(e => e.PersonId), Is.All.EqualTo(PersonId));
            Assert.That(_addedBillEntries.Select(e => e.ActualAmount), Is.All.Null);
            Assert.That(_addedBillEntries.Select(e => e.Paid), Is.All.False);
            Assert.That(_addedBillEntries.Select(e => e.Received), Is.All.False);
            Assert.That(_addedBillEntries.Select(e => e.CreatedAt), Is.All.EqualTo(FixedNow));
        }
    }

    [Test]
    public async Task ProjectYearAsync_OwnBill_SnapshotsFullSplitWithoutPerson()
    {
        // Arrange
        GivenBills(RecurringBill(defaultAmount: 99m));

        // Act
        await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_addedBillEntries.Select(e => e.PlannedAmount), Is.All.EqualTo(99m));
            Assert.That(_addedBillEntries.Select(e => e.SplitRatioSnapshot), Is.All.EqualTo(1m));
            Assert.That(_addedBillEntries.Select(e => e.PersonId), Is.All.Null);
        }
    }

    [Test]
    public async Task ProjectYearAsync_RecurringIncome_SnapshotsPlannedAmount()
    {
        // Arrange
        GivenIncomes(RecurringIncome(defaultAmount: 4321.99m));

        // Act
        await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(_addedIncomeEntries, Has.Count.EqualTo(12));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_addedIncomeEntries.Select(e => e.OwnerId), Is.All.EqualTo(OwnerId));
            Assert.That(_addedIncomeEntries.Select(e => e.IncomeId), Is.All.EqualTo(IncomeId));
            Assert.That(_addedIncomeEntries.Select(e => e.RefYear), Is.All.EqualTo(Year));
            Assert.That(_addedIncomeEntries.Select(e => e.PlannedAmount), Is.All.EqualTo(4321.99m));
            Assert.That(_addedIncomeEntries.Select(e => e.ActualAmount), Is.All.Null);
            Assert.That(_addedIncomeEntries.Select(e => e.Received), Is.All.False);
            Assert.That(_addedIncomeEntries.Select(e => e.CreatedAt), Is.All.EqualTo(FixedNow));
        }
    }

    [Test]
    public async Task ProjectYearAsync_TemplateChangedAfterProjection_EntriesKeepSnapshot()
    {
        // Arrange
        var bill = RecurringBill(defaultAmount: 100m);
        GivenBills(bill);

        // Act
        await _sut.ProjectYearAsync(Year, CancellationToken.None);
        bill.Update(bill.Name, CategoryId, BillKindEnum.Recurring, 999m, 0.5m, PersonId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_addedBillEntries.Select(e => e.PlannedAmount), Is.All.EqualTo(100m));
            Assert.That(_addedBillEntries.Select(e => e.SplitRatioSnapshot), Is.All.EqualTo(1m));
            Assert.That(_addedBillEntries.Select(e => e.PersonId), Is.All.Null);
        }
    }

    [Test]
    public async Task ProjectYearAsync_QueriesExistingEntriesOfRequestedYear()
    {
        // Act
        await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        await _billEntryRepository.Received(1).GetExistingMonthsAsync(Year, Arg.Any<CancellationToken>());
        await _incomeEntryRepository.Received(1).GetExistingMonthsAsync(Year, Arg.Any<CancellationToken>());
    }

    // --- Idempotency ---

    [Test]
    public async Task ProjectYearAsync_AllMonthsAlreadyExist_SkipsEverythingAndDoesNotSave()
    {
        // Arrange
        GivenBills(RecurringBill());
        GivenIncomes(RecurringIncome());
        GivenExistingBillMonths([.. AllMonths.Select(m => (BillId, m))]);
        GivenExistingIncomeMonths([.. AllMonths.Select(m => (IncomeId, m))]);

        // Act
        var result = await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectionDto(Year, 0, 0, 24)));
        Assert.That(_addedBillEntries, Is.Empty);
        Assert.That(_addedIncomeEntries, Is.Empty);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task ProjectYearAsync_SomeMonthsExist_CreatesOnlyMissingMonths()
    {
        // Arrange
        GivenBills(RecurringBill());
        GivenIncomes(RecurringIncome());
        GivenExistingBillMonths((BillId, 1), (BillId, 6), (BillId, 12));
        GivenExistingIncomeMonths((IncomeId, 3));

        // Act
        var result = await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectionDto(Year, 9, 11, 4)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_addedBillEntries.Select(e => e.RefMonth), Is.EqualTo(new[] { 2, 3, 4, 5, 7, 8, 9, 10, 11 }));
            Assert.That(_addedIncomeEntries.Select(e => e.RefMonth), Is.EqualTo(AllMonths.Where(m => m != 3)));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProjectYearAsync_ExistingEntriesOfAnotherTemplate_DoNotSkipThisTemplate()
    {
        // Arrange — the existing entries belong to a different bill/income id (e.g. a one-off or deleted template)
        GivenBills(RecurringBill(BillId));
        GivenIncomes(RecurringIncome(IncomeId));
        GivenExistingBillMonths([.. AllMonths.Select(m => (OtherBillId, m))]);
        GivenExistingIncomeMonths((IncomeId + 1, 1));

        // Act
        var result = await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectionDto(Year, 12, 12, 0)));
    }

    [Test]
    public async Task ProjectYearAsync_OnlyBillsMissing_StillCountsSkippedIncomes()
    {
        // Arrange
        GivenBills(RecurringBill());
        GivenIncomes(RecurringIncome());
        GivenExistingIncomeMonths([.. AllMonths.Select(m => (IncomeId, m))]);

        // Act
        var result = await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectionDto(Year, 12, 0, 12)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Concurrency ---

    [Test]
    public async Task ProjectYearAsync_ConcurrentInsertViolatesUniqueConstraint_ReturnsConflict()
    {
        // Arrange
        GivenBills(RecurringBill());
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintViolationException("ix_bill_entry", new InvalidOperationException()));

        // Act
        var result = await _sut.ProjectYearAsync(Year, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
            Assert.That(result.Error.Message, Does.StartWith(ProjectionService.ProjectionEntity));
        }
    }
}
