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

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class BillServiceTests
{
    private const long OwnerId = 7L;
    private const long BillId = 10L;
    private const long CategoryId = 20L;
    private const long OtherCategoryId = 21L;
    private const long PersonId = 30L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IBillRepository _repository = null!;
    private IBillEntryRepository _entryRepository = null!;
    private ICategoryRepository _categoryRepository = null!;
    private IPersonRepository _personRepository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private BillService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IBillRepository>();
        _entryRepository = Substitute.For<IBillEntryRepository>();
        _categoryRepository = Substitute.For<ICategoryRepository>();
        _categoryRepository.ExistsByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _personRepository = Substitute.For<IPersonRepository>();
        _personRepository.ExistsByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new BillService(
            _repository, _entryRepository, _categoryRepository, _personRepository,
            _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private static Bill ExistingBill(decimal defaultAmount = 100m, decimal splitRatio = 1m, long? personId = null) =>
        EntityId.With(Bill.Create(OwnerId, "Aluguel", CategoryId, BillKindEnum.Recurring, defaultAmount, splitRatio, personId, FixedNow), BillId);

    private static BillEntry Entry(int year, int month, decimal planned, decimal splitRatio = 1m, bool paid = false, decimal? actual = null)
    {
        var entry = BillEntry.Create(OwnerId, BillId, year, month, planned, splitRatio, splitRatio < 1m ? PersonId : null, FixedNow);
        if (paid)
            entry.MarkPaid(FixedNow, actual);
        return entry;
    }

    private void GivenBill(Bill bill) =>
        _repository.GetByIdAsync(bill.Id, Arg.Any<CancellationToken>()).Returns(bill);

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

    private static void AssertNotFound(Result result, string entity)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
            Assert.That(result.Error.Message, Does.StartWith(entity));
        }
    }

    private async Task AssertNothingPersistedAsync()
    {
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // --- CreateAsync: validation ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task CreateAsync_BlankName_ReturnsValidationErrorWithoutPersisting(string? name)
    {
        // Act
        var result = await _sut.CreateAsync(name!, CategoryId, BillKindEnum.Recurring, 100m, 1m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.NameField);
        await AssertNothingPersistedAsync();
    }

    [TestCase(-1)]
    [TestCase(2)]
    public async Task CreateAsync_UndefinedKind_ReturnsValidationErrorWithoutPersisting(int kind)
    {
        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, (BillKindEnum)kind, 100m, 1m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.KindField);
        await AssertNothingPersistedAsync();
    }

    [TestCase(-0.01)]
    [TestCase(-1000)]
    public async Task CreateAsync_NegativeDefaultAmount_ReturnsValidationErrorWithoutPersisting(decimal amount)
    {
        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, BillKindEnum.Recurring, amount, 1m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.DefaultAmountField);
        await AssertNothingPersistedAsync();
    }

    [TestCase(-0.01)]
    [TestCase(1.01)]
    [TestCase(1.5)]
    public async Task CreateAsync_SplitRatioOutOfRange_ReturnsValidationErrorWithoutPersisting(decimal splitRatio)
    {
        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, BillKindEnum.Recurring, 100m, splitRatio, PersonId, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.SplitRatioField);
        await AssertNothingPersistedAsync();
    }

    [TestCase(0)]
    [TestCase(0.5)]
    [TestCase(0.99)]
    public async Task CreateAsync_SplitBelowOneWithoutPerson_ReturnsValidationErrorWithoutPersisting(decimal splitRatio)
    {
        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, BillKindEnum.Recurring, 100m, splitRatio, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.PersonIdField);
        await AssertNothingPersistedAsync();
    }

    [Test]
    public async Task CreateAsync_SplitOneWithPerson_ReturnsValidationErrorWithoutPersisting()
    {
        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, BillKindEnum.Recurring, 100m, 1m, PersonId, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.PersonIdField);
        await AssertNothingPersistedAsync();
    }

    [Test]
    public async Task CreateAsync_AllFieldsInvalid_ReturnsOneValidationErrorPerFieldWithoutLookups()
    {
        // Act
        var result = await _sut.CreateAsync(" ", CategoryId, (BillKindEnum)9, -1m, 0.5m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result,
            BillService.NameField, BillService.KindField, BillService.DefaultAmountField, BillService.PersonIdField);
        await _categoryRepository.DidNotReceiveWithAnyArgs().ExistsByIdAsync(default, default);
        await AssertNothingPersistedAsync();
    }

    // --- CreateAsync: references ---

    [Test]
    public async Task CreateAsync_CategoryNotFound_ReturnsNotFoundWithoutPersisting()
    {
        // Arrange
        _categoryRepository.ExistsByIdAsync(CategoryId, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, BillKindEnum.Recurring, 100m, 1m, null, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.CategoryEntity);
        await AssertNothingPersistedAsync();
    }

    [Test]
    public async Task CreateAsync_PersonNotFound_ReturnsNotFoundWithoutPersisting()
    {
        // Arrange
        _personRepository.ExistsByIdAsync(PersonId, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, BillKindEnum.Recurring, 100m, 0.5m, PersonId, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.PersonEntity);
        await AssertNothingPersistedAsync();
    }

    [Test]
    public async Task CreateAsync_WithoutPerson_DoesNotLookUpPerson()
    {
        // Act
        var result = await _sut.CreateAsync("Aluguel", CategoryId, BillKindEnum.Recurring, 100m, 1m, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        await _personRepository.DidNotReceiveWithAnyArgs().ExistsByIdAsync(default, default);
    }

    // --- CreateAsync: success ---

    [TestCase(BillKindEnum.Recurring, 1500, 1, null)]
    [TestCase(BillKindEnum.OneOff, 0, 0.5, PersonId)]
    [TestCase(BillKindEnum.Recurring, 80, 0, PersonId)]
    public async Task CreateAsync_ValidData_AddsBillForCurrentOwnerAndSaves(
        BillKindEnum kind, decimal amount, decimal splitRatio, long? personId)
    {
        // Arrange
        Bill? added = null;
        _repository.Add(Arg.Do<Bill>(b => added = b));

        // Act
        var result = await _sut.CreateAsync("  Aluguel  ", CategoryId, kind, amount, splitRatio, personId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(new BillDto(0L, "Aluguel", CategoryId, kind, amount, splitRatio, personId)));
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(added.CreatedAt, Is.EqualTo(FixedNow));
            Assert.That(added.Active, Is.True);
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- GetAllByNameAsync ---

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsBills_ReturnsThemOrderedByNameQuery()
    {
        // Arrange
        BillDto[] bills = [new(1, "Aluguel", CategoryId, BillKindEnum.Recurring, 1500m, 1m, null)];
        IPagedQuery<Bill>? query = null;
        _repository.GetAllByNameAsync(Arg.Do<IPagedQuery<Bill>>(q => query = q), Arg.Any<CancellationToken>()).Returns(bills);

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(bills));
            Assert.That(query!.Take, Is.EqualTo(1000));
            Assert.That(query.Skip, Is.Zero);
            Assert.That(query.OrderBy.Compile()(ExistingBill()), Is.EqualTo("Aluguel"));
        }
    }

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsNull_ReturnsEmpty()
    {
        // Arrange
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Bill>>(), Arg.Any<CancellationToken>())
            .Returns((IEnumerable<BillDto>)null!);

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    // --- UpdateAsync ---

    [Test]
    public async Task UpdateAsync_InvalidData_ReturnsValidationErrorWithoutLookup()
    {
        // Act
        var result = await _sut.UpdateAsync(BillId, "", CategoryId, BillKindEnum.Recurring, 100m, 2m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.NameField, BillService.SplitRatioField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_SplitBelowOneWithoutPerson_ReturnsValidationError()
    {
        // Act
        var result = await _sut.UpdateAsync(BillId, "Aluguel", CategoryId, BillKindEnum.Recurring, 100m, 0.5m, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.PersonIdField);
    }

    [Test]
    public async Task UpdateAsync_BillNotFound_ReturnsNotFoundWithoutSaving()
    {
        // Act
        var result = await _sut.UpdateAsync(BillId, "Aluguel", CategoryId, BillKindEnum.Recurring, 100m, 1m, null, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.BillEntity);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_CategoryNotFound_ReturnsNotFoundAndKeepsBill()
    {
        // Arrange
        var bill = ExistingBill();
        GivenBill(bill);
        _categoryRepository.ExistsByIdAsync(OtherCategoryId, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _sut.UpdateAsync(BillId, "Carro", OtherCategoryId, BillKindEnum.OneOff, 5m, 1m, null, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.CategoryEntity);
        Assert.That(bill.Name, Is.EqualTo("Aluguel"));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_PersonNotFound_ReturnsNotFoundAndKeepsBill()
    {
        // Arrange
        var bill = ExistingBill();
        GivenBill(bill);
        _personRepository.ExistsByIdAsync(PersonId, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _sut.UpdateAsync(BillId, "Aluguel", CategoryId, BillKindEnum.Recurring, 100m, 0.5m, PersonId, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.PersonEntity);
        Assert.That(bill.SplitRatio, Is.EqualTo(1m));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_ValidData_UpdatesBillSavesAndReturnsDto()
    {
        // Arrange
        var bill = ExistingBill();
        GivenBill(bill);

        // Act
        var result = await _sut.UpdateAsync(BillId, "  Carro  ", OtherCategoryId, BillKindEnum.OneOff, 800m, 0.5m, PersonId, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(new BillDto(BillId, "Carro", OtherCategoryId, BillKindEnum.OneOff, 800m, 0.5m, PersonId)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- DeleteByIdAsync ---

    [Test]
    public async Task DeleteByIdAsync_BillNotFound_ReturnsNotFoundWithoutSaving()
    {
        // Act
        var result = await _sut.DeleteByIdAsync(BillId, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.BillEntity);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task DeleteByIdAsync_ExistingBill_SoftDeletesAndSaves()
    {
        // Arrange
        var bill = ExistingBill();
        GivenBill(bill);

        // Act
        var result = await _sut.DeleteByIdAsync(BillId, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(bill.Active, Is.False);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- RecalculateAsync ---

    [TestCase(0)]
    [TestCase(13)]
    [TestCase(-1)]
    public async Task RecalculateAsync_InvalidMonth_ReturnsValidationErrorWithoutLookup(int month)
    {
        // Act
        var result = await _sut.RecalculateAsync(BillId, 2026, month, 100m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.FromMonthField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task RecalculateAsync_NegativeAmount_ReturnsValidationError()
    {
        // Act
        var result = await _sut.RecalculateAsync(BillId, 2026, 7, -1m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.NewAmountField);
    }

    [Test]
    public async Task RecalculateAsync_AllFieldsInvalid_ReturnsOneErrorPerField()
    {
        // Act
        var result = await _sut.RecalculateAsync(BillId, 2026, 0, -1m, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, BillService.FromMonthField, BillService.NewAmountField);
    }

    [Test]
    public async Task RecalculateAsync_BillNotFound_ReturnsNotFoundWithoutSaving()
    {
        // Act
        var result = await _sut.RecalculateAsync(BillId, 2026, 7, 100m, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.BillEntity);
        await _entryRepository.DidNotReceiveWithAnyArgs().GetByBillFromMonthAsync(default, default, default, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task RecalculateAsync_MixedEntries_UpdatesUnpaidSkipsPaidAndUpdatesDefaultAmount()
    {
        // Arrange
        var bill = ExistingBill(defaultAmount: 100m);
        GivenBill(bill);
        var unpaidJul = Entry(2026, 7, 100m);
        var paidAug = Entry(2026, 8, 100m, paid: true, actual: 110m);
        var unpaidJan = Entry(2027, 1, 100m);
        _entryRepository.GetByBillFromMonthAsync(BillId, 2026, 7, Arg.Any<CancellationToken>())
            .Returns([unpaidJul, paidAug, unpaidJan]);

        // Act
        var result = await _sut.RecalculateAsync(BillId, 2026, 7, 175m, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(new BillRecalculationDto(BillId, 2, 1, 175m)));
            Assert.That(bill.DefaultAmount, Is.EqualTo(175m));
            Assert.That(unpaidJul.PlannedAmount, Is.EqualTo(175m));
            Assert.That(unpaidJan.PlannedAmount, Is.EqualTo(175m));
            Assert.That(paidAug.PlannedAmount, Is.EqualTo(100m));
            Assert.That(paidAug.ActualAmount, Is.EqualTo(110m));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecalculateAsync_NoEntriesInRange_UpdatesOnlyDefaultAmount()
    {
        // Arrange
        var bill = ExistingBill(defaultAmount: 100m);
        GivenBill(bill);
        _entryRepository.GetByBillFromMonthAsync(BillId, 2026, 1, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await _sut.RecalculateAsync(BillId, 2026, 1, 0m, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new BillRecalculationDto(BillId, 0, 0, 0m)));
        Assert.That(bill.DefaultAmount, Is.Zero);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- GetHistoryAsync ---

    [Test]
    public async Task GetHistoryAsync_HeaderNotFound_ReturnsNotFoundWithoutLoadingEntries()
    {
        // Act
        var result = await _sut.GetHistoryAsync(BillId, null, null, null, null, CancellationToken.None);

        // Assert
        AssertNotFound(result, BillService.BillEntity);
        await _repository.Received(1).GetHistoryHeaderAsync(BillId, OwnerId, Arg.Any<CancellationToken>());
        await _entryRepository.DidNotReceiveWithAnyArgs().GetByBillAsync(default, default);
    }

    [Test]
    public async Task GetHistoryAsync_WithEntries_ReturnsHeaderChronologicalItemsAndSummary()
    {
        // Arrange
        _repository.GetHistoryHeaderAsync(BillId, OwnerId, Arg.Any<CancellationToken>())
            .Returns(new BillHistoryHeaderDto(BillId, "Aluguel", "Moradia", 0.5m, "Esposa"));
        _entryRepository.GetByBillAsync(BillId, Arg.Any<CancellationToken>()).Returns(
        [
            Entry(2026, 2, 200m, splitRatio: 0.5m),
            Entry(2026, 1, 100m, splitRatio: 0.5m, paid: true),
        ]);

        // Act
        var result = await _sut.GetHistoryAsync(BillId, null, null, null, null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var history = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(history.BillId, Is.EqualTo(BillId));
            Assert.That(history.Name, Is.EqualTo("Aluguel"));
            Assert.That(history.Category, Is.EqualTo("Moradia"));
            Assert.That(history.SplitRatio, Is.EqualTo(0.5m));
            Assert.That(history.Person, Is.EqualTo("Esposa"));
            Assert.That(history.Items.Select(i => i.Month), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(history.Items[0].Variation, Is.Null);
            Assert.That(history.Items[0].MyShare, Is.EqualTo(50m));
            Assert.That(history.Items[0].Paid, Is.True);
            Assert.That(history.Items[1].Variation, Is.EqualTo(new BillHistoryVariationDto(100m, 100m)));
            Assert.That(history.Summary, Is.EqualTo(new BillHistorySummaryDto(150m, 100m, 200m, 50m)));
        }
    }

    [Test]
    public async Task GetHistoryAsync_PeriodFilter_KeepsOnlyEntriesInWindow()
    {
        // Arrange
        _repository.GetHistoryHeaderAsync(BillId, OwnerId, Arg.Any<CancellationToken>())
            .Returns(new BillHistoryHeaderDto(BillId, "Aluguel", "Moradia", 1m, null));
        _entryRepository.GetByBillAsync(BillId, Arg.Any<CancellationToken>()).Returns(
            [Entry(2026, 1, 100m), Entry(2026, 5, 100m), Entry(2026, 9, 100m)]);

        // Act
        var result = await _sut.GetHistoryAsync(BillId, 2026, 3, 2026, 6, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Items.Select(i => i.Month), Is.EqualTo(new[] { 5 }));
        Assert.That(result.Value.Person, Is.Null);
    }

    [Test]
    public async Task GetHistoryAsync_NoEntries_ReturnsEmptyItemsAndZeroSummary()
    {
        // Arrange
        _repository.GetHistoryHeaderAsync(BillId, OwnerId, Arg.Any<CancellationToken>())
            .Returns(new BillHistoryHeaderDto(BillId, "Aluguel", "Moradia", 1m, null));
        _entryRepository.GetByBillAsync(BillId, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await _sut.GetHistoryAsync(BillId, null, null, null, null, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Items, Is.Empty);
        Assert.That(result.Value.Summary, Is.EqualTo(new BillHistorySummaryDto(0m, 0m, 0m, 0m)));
    }
}
