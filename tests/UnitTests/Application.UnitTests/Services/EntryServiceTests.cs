using Application.Abstractions.Repositories;
using Application.DTOs.Services;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using NSubstitute;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class EntryServiceTests
{
    private const long OwnerId = 7L;
    private const long PersonId = 30L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IBillEntryRepository _billEntryRepository = null!;
    private IIncomeEntryRepository _incomeEntryRepository = null!;
    private ICurrentOwner _currentOwner = null!;
    private EntryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _billEntryRepository = Substitute.For<IBillEntryRepository>();
        _billEntryRepository.GetMonthWithNamesAsync(default, default, default, default).ReturnsForAnyArgs([]);
        _incomeEntryRepository = Substitute.For<IIncomeEntryRepository>();
        _incomeEntryRepository.GetMonthWithNameAsync(default, default, default, default).ReturnsForAnyArgs([]);
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _sut = new EntryService(_billEntryRepository, _incomeEntryRepository, _currentOwner);
    }

    private static BillEntry Bill(long id, decimal planned, decimal split = 1m, decimal? actual = null, bool paid = false, bool received = false)
    {
        var entry = EntityId.With(BillEntry.Create(OwnerId, 100 + id, 2026, 3, planned, split, split < 1m ? PersonId : null, FixedNow), id);
        if (actual.HasValue)
            entry.UpdateAmounts(null, actual);
        if (paid)
            entry.MarkPaid(FixedNow, actual);
        if (received)
            entry.MarkReceived(FixedNow);
        return entry;
    }

    private static IncomeEntry Income(long id, decimal planned, decimal? actual = null, bool received = false)
    {
        var entry = EntityId.With(IncomeEntry.Create(OwnerId, 200 + id, 2026, 3, planned, FixedNow), id);
        if (actual.HasValue)
            entry.UpdateAmounts(null, actual);
        if (received)
            entry.MarkReceived(FixedNow, actual);
        return entry;
    }

    private void GivenBills(params BillEntryWithNamesDto[] rows) =>
        _billEntryRepository.GetMonthWithNamesAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>()).Returns(rows);

    private void GivenIncomes(params IncomeEntryWithNameDto[] rows) =>
        _incomeEntryRepository.GetMonthWithNameAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>()).Returns(rows);

    private static void AssertValidationFailure(Result result, params string[] expectedCodes)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.InstanceOf<ValidationError>());
        }
        Assert.That(((ValidationError)result.Error).Errors.Select(e => e.Code), Is.EquivalentTo(expectedCodes));
    }

    [TestCase(null, 3, EntryValidation.YearField)]
    [TestCase(1999, 3, EntryValidation.YearField)]
    [TestCase(2101, 3, EntryValidation.YearField)]
    [TestCase(2026, null, EntryValidation.MonthField)]
    [TestCase(2026, 0, EntryValidation.MonthField)]
    [TestCase(2026, 13, EntryValidation.MonthField)]
    public async Task GetMonthAsync_InvalidPeriod_ReturnsValidationErrorWithoutQuerying(int? year, int? month, string field)
    {
        // Act
        var result = await _sut.GetMonthAsync(year, month, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, field);
        await _billEntryRepository.DidNotReceiveWithAnyArgs().GetMonthWithNamesAsync(default, default, default, default);
        await _incomeEntryRepository.DidNotReceiveWithAnyArgs().GetMonthWithNameAsync(default, default, default, default);
    }

    [Test]
    public async Task GetMonthAsync_BothMissing_ReturnsBothFieldErrors()
    {
        // Act
        var result = await _sut.GetMonthAsync(null, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, EntryValidation.YearField, EntryValidation.MonthField);
    }

    [Test]
    public async Task GetMonthAsync_EmptyMonth_ReturnsEmptyListsAndZeroTotals()
    {
        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That((result.Value.Year, result.Value.Month), Is.EqualTo((2026, 3)));
            Assert.That(result.Value.Bills, Is.Empty);
            Assert.That(result.Value.Incomes, Is.Empty);
            Assert.That(result.Value.Totals, Is.EqualTo(new MonthTotalsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)));
        }
    }

    [Test]
    public async Task GetMonthAsync_ValidPeriod_QueriesRepositoriesWithCurrentOwner()
    {
        // Act
        await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        await _billEntryRepository.Received(1).GetMonthWithNamesAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>());
        await _incomeEntryRepository.Received(1).GetMonthWithNameAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMonthAsync_Bills_AreSortedByCategoryThenName()
    {
        // Arrange
        GivenBills(
            new(Bill(1, 10m), "Zeta", "Moradia", null),
            new(Bill(2, 10m), "Alpha", "Moradia", null),
            new(Bill(3, 10m), "Beta", "Lazer", null));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Bills.Select(b => b.Id), Is.EqualTo(new long[] { 3, 2, 1 }));
    }

    [Test]
    public async Task GetMonthAsync_SplitBill_MapsNamesAndDerivedValues()
    {
        // Arrange
        var entry = Bill(1, 200m, split: 0.5m, actual: 180m, paid: true, received: true);
        GivenBills(new BillEntryWithNamesDto(entry, "Luz", "Moradia", "Ana"));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Bills.Single(), Is.EqualTo(new BillEntryListItemDto(
            1, 101, "Luz", "Moradia", 200m, 180m, 0.5m, "Ana", 180m, 90m, 90m,
            true, entry.PaidDate, true, entry.ReceivedDate)));
    }

    [Test]
    public async Task GetMonthAsync_BillWithoutPerson_HidesPersonName()
    {
        // Arrange — a stale name must never leak when the snapshot has no person
        GivenBills(new BillEntryWithNamesDto(Bill(1, 50m), "Netflix", "Lazer", "Stale"));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        var bill = result.Value.Bills.Single();
        Assert.That((bill.Person, bill.EffectiveAmount, bill.MyShare, bill.Receivable), Is.EqualTo(((string?)null, 50m, 50m, 0m)));
    }

    [Test]
    public async Task GetMonthAsync_Incomes_MapNameAndEffectiveAmount()
    {
        // Arrange
        var received = Income(1, 1000m, actual: 1100m, received: true);
        GivenIncomes(new(received, "Salário"), new(Income(2, 300m), "Freela"));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Incomes, Is.EqualTo(new[]
        {
            new IncomeEntryListItemDto(1, 201, "Salário", 1000m, 1100m, 1100m, true, received.ReceivedDate),
            new IncomeEntryListItemDto(2, 202, "Freela", 300m, null, 300m, false, null),
        }));
    }

    [Test]
    public async Task GetMonthAsync_MixedMonth_ComputesTotalsAndBalances()
    {
        // Arrange
        GivenBills(
            new(Bill(1, 200m, split: 0.5m, actual: 180m, paid: true, received: true), "Luz", "Moradia", "Ana"),   // eff 180, share 90, recv 90 received
            new(Bill(2, 100m, split: 0.5m), "Água", "Moradia", "Ana"),                                              // eff 100, share 50, recv 50 pending
            new(Bill(3, 300m, actual: 320m, paid: true), "Aluguel", "Moradia", null));                             // eff 320, share 320
        GivenIncomes(
            new(Income(1, 1000m, actual: 1100m, received: true), "Salário"),
            new(Income(2, 300m), "Freela"));

        // Act
        var totals = (await _sut.GetMonthAsync(2026, 3, CancellationToken.None)).Value.Totals;

        // Assert
        // saldoPrevistoOtimista = 1300 − (100 + 50 + 300) = 850; piorCaso = 850 − 50 = 800
        // saldoRealizado = (1100 + 90) − (180 + 320) = 690
        Assert.That(totals, Is.EqualTo(new MonthTotalsDto(
            BillsPlanned: 600m, BillsEffective: 600m, MyShare: 460m,
            Receivable: 50m, Received: 90m, ReceivablePending: 50m, ReceivableReceived: 90m, PaidFull: 500m,
            IncomesPlanned: 1300m, IncomesEffective: 1400m, IncomesReceived: 1100m,
            SaldoPrevisto: 850m, SaldoReal: 690m,
            SaldoPrevistoOtimista: 850m, SaldoPrevistoPiorCaso: 800m, SaldoRealizado: 690m)));
    }
}
