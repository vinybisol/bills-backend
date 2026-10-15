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
public sealed class BillEntryServiceTests
{
    private const long OwnerId = 7L;
    private const long BillId = 10L;
    private const long EntryId = 40L;
    private const long CategoryId = 20L;
    private const long PersonId = 30L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IBillEntryRepository _repository = null!;
    private IBillRepository _billRepository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private BillEntryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IBillEntryRepository>();
        _billRepository = Substitute.For<IBillRepository>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new BillEntryService(_repository, _billRepository, _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private void GivenBill(BillKindEnum kind = BillKindEnum.OneOff, decimal defaultAmount = 300m, decimal splitRatio = 0.5m, long? personId = PersonId)
    {
        var bill = EntityId.With(Bill.Create(OwnerId, "IPVA", CategoryId, kind, defaultAmount, splitRatio, personId, FixedNow), BillId);
        _billRepository.GetByIdAsync(BillId, Arg.Any<CancellationToken>()).Returns(bill);
    }

    private BillEntry GivenEntry(bool paid = false, bool received = false, decimal planned = 100m, decimal? actual = null)
    {
        var entry = EntityId.With(BillEntry.Create(OwnerId, BillId, 2026, 3, planned, 0.5m, PersonId, FixedNow), EntryId);
        if (actual.HasValue)
            entry.UpdateAmounts(null, actual);
        if (paid)
            entry.MarkPaid(FixedNow.AddDays(-3), actual);
        if (received)
            entry.MarkReceived(FixedNow.AddDays(-1));
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
    public async Task CreateAsync_OneOffBill_PersistsEntryWithSnapshots()
    {
        // Arrange
        GivenBill(defaultAmount: 300m, splitRatio: 0.5m, personId: PersonId);
        BillEntry? added = null;
        _repository.Add(Arg.Do<BillEntry>(e => added = e));

        // Act
        var result = await _sut.CreateAsync(BillId, 2026, 4, 250m, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(added.CreatedAt, Is.EqualTo(FixedNow));
            Assert.That(result.Value, Is.EqualTo(new BillEntryDto(0, BillId, 2026, 4, 250m, null, 0.5m, PersonId, false, null, false, null)));
        }
    }

    [Test]
    public async Task CreateAsync_NullPlannedAmount_UsesTemplateDefaultAmount()
    {
        // Arrange
        GivenBill(defaultAmount: 300m, splitRatio: 1m, personId: null);

        // Act
        var result = await _sut.CreateAsync(BillId, 2026, 4, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.PlannedAmount, Is.EqualTo(300m));
            Assert.That(result.Value.SplitRatioSnapshot, Is.EqualTo(1m));
            Assert.That(result.Value.PersonId, Is.Null);
        }
    }

    [TestCase(2026, 0, 1.0, EntryValidation.MonthField)]
    [TestCase(2026, 13, 1.0, EntryValidation.MonthField)]
    [TestCase(1999, 1, 1.0, EntryValidation.YearField)]
    [TestCase(2101, 1, 1.0, EntryValidation.YearField)]
    [TestCase(2026, 1, -0.01, EntryValidation.PlannedAmountField)]
    public async Task CreateAsync_InvalidInput_ReturnsValidationErrorWithoutTouchingRepositories(int year, int month, decimal planned, string field)
    {
        // Act
        var result = await _sut.CreateAsync(BillId, year, month, planned, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, field);
        await _billRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task CreateAsync_AllFieldsInvalid_ReturnsEveryFieldError()
    {
        // Act
        var result = await _sut.CreateAsync(BillId, 1, 0, -1m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, EntryValidation.YearField, EntryValidation.MonthField, EntryValidation.PlannedAmountField);
    }

    [Test]
    public async Task CreateAsync_BillNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.CreateAsync(BillId, 2026, 4, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        Assert.That(result.Error.Message, Does.StartWith(BillEntryService.BillEntity));
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task CreateAsync_RecurringBill_ReturnsValidationErrorOnBillId()
    {
        // Arrange
        GivenBill(kind: BillKindEnum.Recurring);

        // Act
        var result = await _sut.CreateAsync(BillId, 2026, 4, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillEntryService.BillIdField);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task CreateAsync_DuplicateMonth_ReturnsConflict()
    {
        // Arrange
        GivenBill();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new UniqueConstraintViolationException("ux", new InvalidOperationException())));

        // Act
        var result = await _sut.CreateAsync(BillId, 2026, 4, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.Conflict);
    }

    // --- DeleteByIdAsync ---

    [Test]
    public async Task DeleteByIdAsync_UnpaidEntry_RemovesAndSaves()
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
    public async Task DeleteByIdAsync_UnpaidButReceivedEntry_RemovesIt()
    {
        // Arrange — only "paid" freezes a bill entry; "received" is independent
        var entry = GivenEntry(received: true);

        // Act
        var result = await _sut.DeleteByIdAsync(EntryId, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _repository.Received(1).Remove(entry);
    }

    [Test]
    public async Task DeleteByIdAsync_PaidEntry_ReturnsFrozenConflict()
    {
        // Arrange
        GivenEntry(paid: true);

        // Act
        var result = await _sut.DeleteByIdAsync(EntryId, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(BillEntryService.Frozen));
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
        Assert.That(result.Error.Message, Does.StartWith(BillEntryService.BillEntryEntity));
        await AssertNotSavedAsync();
    }

    // --- UpdateAmountsAsync ---

    [Test]
    public async Task UpdateAmountsAsync_UnpaidEntry_UpdatesBothAmounts()
    {
        // Arrange
        var entry = GivenEntry();

        // Act
        var result = await _sut.UpdateAmountsAsync(EntryId, 120m, 115m, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.PlannedAmount, Is.EqualTo(120m));
            Assert.That(result.Value.ActualAmount, Is.EqualTo(115m));
            Assert.That(entry.SplitRatioSnapshot, Is.EqualTo(0.5m));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateAmountsAsync_NullAmounts_KeepsCurrentValues()
    {
        // Arrange
        GivenEntry(planned: 100m, actual: 90m);

        // Act
        var result = await _sut.UpdateAmountsAsync(EntryId, null, null, CancellationToken.None);

        // Assert
        Assert.That((result.Value.PlannedAmount, result.Value.ActualAmount), Is.EqualTo((100m, (decimal?)90m)));
    }

    [Test]
    public async Task UpdateAmountsAsync_PaidEntry_ReturnsFrozenConflictAndKeepsAmounts()
    {
        // Arrange
        var entry = GivenEntry(paid: true, actual: 95m);

        // Act
        var result = await _sut.UpdateAmountsAsync(EntryId, 200m, 199m, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(BillEntryService.Frozen));
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

    // --- PayAsync ---

    [Test]
    public async Task PayAsync_WithoutAmountOrDate_PaysPlannedNow()
    {
        // Arrange
        GivenEntry(planned: 100m);

        // Act
        var result = await _sut.PayAsync(EntryId, null, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Paid, Is.True);
            Assert.That(result.Value.ActualAmount, Is.EqualTo(100m));
            Assert.That(result.Value.PaidDate, Is.EqualTo(FixedNow));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PayAsync_WithAmountAndDate_RecordsThemAtMidnightUtc()
    {
        // Arrange
        GivenEntry();

        // Act
        var result = await _sut.PayAsync(EntryId, 87.5m, new DateOnly(2026, 3, 10), CancellationToken.None);

        // Assert
        Assert.That((result.Value.ActualAmount, result.Value.PaidDate),
            Is.EqualTo(((decimal?)87.5m, (DateTimeOffset?)new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero))));
    }

    [Test]
    public async Task PayAsync_ReceivedEntry_KeepsReceivedUntouched()
    {
        // Arrange
        var entry = GivenEntry(received: true);
        var receivedDate = entry.ReceivedDate;

        // Act
        var result = await _sut.PayAsync(EntryId, null, null, CancellationToken.None);

        // Assert
        Assert.That((result.Value.Paid, result.Value.Received, result.Value.ReceivedDate), Is.EqualTo((true, true, receivedDate)));
    }

    [Test]
    public async Task PayAsync_AlreadyPaid_ReturnsFrozenConflictAndKeepsValues()
    {
        // Arrange
        var entry = GivenEntry(paid: true, actual: 95m);
        var paidDate = entry.PaidDate;

        // Act
        var result = await _sut.PayAsync(EntryId, 1m, new DateOnly(2026, 1, 1), CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.EqualTo(BillEntryService.Frozen));
        Assert.That((entry.ActualAmount, entry.PaidDate), Is.EqualTo(((decimal?)95m, paidDate)));
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task PayAsync_NegativeActualAmount_ReturnsValidationError()
    {
        // Act
        var result = await _sut.PayAsync(EntryId, -1m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, EntryValidation.ActualAmountField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task PayAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.PayAsync(EntryId, null, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        await AssertNotSavedAsync();
    }

    // --- UnpayAsync ---

    [Test]
    public async Task UnpayAsync_PaidAndReceivedEntry_UnfreezesPaidOnly()
    {
        // Arrange
        var entry = GivenEntry(paid: true, received: true, actual: 95m);

        // Act
        var result = await _sut.UnpayAsync(EntryId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Paid, Is.False);
            Assert.That(result.Value.PaidDate, Is.Null);
            Assert.That(result.Value.ActualAmount, Is.EqualTo(95m));
            Assert.That(result.Value.Received, Is.True);
            Assert.That(result.Value.ReceivedDate, Is.EqualTo(entry.ReceivedDate));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnpayAsync_UnpaidEntry_IsIdempotent()
    {
        // Arrange
        GivenEntry();

        // Act
        var result = await _sut.UnpayAsync(EntryId, CancellationToken.None);

        // Assert
        Assert.That((result.IsSuccess, result.Value.Paid), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task UnpayAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.UnpayAsync(EntryId, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        await AssertNotSavedAsync();
    }
}
