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
public sealed class DashboardServiceTests
{
    private const long OwnerId = 7L;
    private const long PersonId = 30L;
    private const long MoradiaId = 1L;
    private const long LazerId = 2L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IBillEntryRepository _billEntryRepository = null!;
    private IIncomeEntryRepository _incomeEntryRepository = null!;
    private ICurrentOwner _currentOwner = null!;
    private DashboardService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _billEntryRepository = Substitute.For<IBillEntryRepository>();
        _billEntryRepository.GetMonthWithNamesAsync(default, default, default, default).ReturnsForAnyArgs([]);
        _billEntryRepository.GetYearWithNamesAsync(default, default, default).ReturnsForAnyArgs([]);
        _incomeEntryRepository = Substitute.For<IIncomeEntryRepository>();
        _incomeEntryRepository.GetMonthWithNameAsync(default, default, default, default).ReturnsForAnyArgs([]);
        _incomeEntryRepository.GetYearWithNameAsync(default, default, default).ReturnsForAnyArgs([]);
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _sut = new DashboardService(_billEntryRepository, _incomeEntryRepository, _currentOwner);
    }

    private static BillEntry Bill(
        long id, decimal planned, decimal split = 1m, decimal? actual = null,
        bool paid = false, bool received = false, int month = 3)
    {
        var entry = EntityId.With(
            BillEntry.Create(OwnerId, 100 + id, 2026, month, planned, split, split < 1m ? PersonId : null, FixedNow), id);
        if (actual.HasValue)
            entry.UpdateAmounts(null, actual);
        if (paid)
            entry.MarkPaid(FixedNow, actual);
        if (received)
            entry.MarkReceived(FixedNow);
        return entry;
    }

    private static IncomeEntry Income(long id, decimal planned, decimal? actual = null, bool received = false, int month = 3)
    {
        var entry = EntityId.With(IncomeEntry.Create(OwnerId, 200 + id, 2026, month, planned, FixedNow), id);
        if (actual.HasValue)
            entry.UpdateAmounts(null, actual);
        if (received)
            entry.MarkReceived(FixedNow, actual);
        return entry;
    }

    private static BillEntryWithNamesDto Row(BillEntry entry, long categoryId = MoradiaId, string? category = null) =>
        new(entry, $"Bill {entry.Id}", categoryId, category ?? (categoryId == MoradiaId ? "Moradia" : "Lazer"),
            entry.PersonId.HasValue ? "Ana" : null);

    private static IncomeEntryWithNameDto Row(IncomeEntry entry) => new(entry, $"Income {entry.Id}");

    private void GivenMonth(BillEntryWithNamesDto[] bills, params IncomeEntryWithNameDto[] incomes)
    {
        _billEntryRepository.GetMonthWithNamesAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>()).Returns(bills);
        _incomeEntryRepository.GetMonthWithNameAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>()).Returns(incomes);
    }

    private void GivenYear(BillEntryWithNamesDto[] bills, params IncomeEntryWithNameDto[] incomes)
    {
        _billEntryRepository.GetYearWithNamesAsync(2026, OwnerId, Arg.Any<CancellationToken>()).Returns(bills);
        _incomeEntryRepository.GetYearWithNameAsync(2026, OwnerId, Arg.Any<CancellationToken>()).Returns(incomes);
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

    // --- GetMonthAsync ---

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
    public async Task GetMonthAsync_NoYearNorMonth_ReturnsBothFieldErrors()
    {
        // Act
        var result = await _sut.GetMonthAsync(null, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, EntryValidation.YearField, EntryValidation.MonthField);
    }

    [Test]
    public async Task GetMonthAsync_ValidPeriod_QueriesCurrentOwnersMonth()
    {
        // Act
        await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        await _billEntryRepository.Received(1).GetMonthWithNamesAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>());
        await _incomeEntryRepository.Received(1).GetMonthWithNameAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMonthAsync_NoEntries_ReturnsZeroedSummaryAndEmptyByCategory()
    {
        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var dto = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((dto.Year, dto.Month), Is.EqualTo((2026, 3)));
            Assert.That(dto.Summary, Is.EqualTo(new DashboardSummaryDto(
                0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0m, 0m, 0m, 0m, 0m, 0m)));
            Assert.That(dto.ByCategory, Is.Empty);
        }
    }

    [Test]
    public async Task GetMonthAsync_SplitPaidBill_UsesMyShareForExpenseAndFullValueForPaidFull()
    {
        // Arrange — planned 1000 split 0.5, paid 900; income planned 2000 received 2100.
        GivenMonth(
            [Row(Bill(1, 1000m, split: 0.5m, actual: 900m, paid: true))],
            Row(Income(1, 2000m, actual: 2100m, received: true)));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        var s = result.Value.Summary;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s.PlannedExpense, Is.EqualTo(500m));
            Assert.That(s.ActualExpense, Is.EqualTo(450m));
            Assert.That(s.PlannedIncome, Is.EqualTo(2000m));
            Assert.That(s.ActualIncome, Is.EqualTo(2100m));
            Assert.That(s.ReceivablePending, Is.EqualTo(450m));
            Assert.That(s.ReceivableReceived, Is.Zero);
            Assert.That(s.PaidFull, Is.EqualTo(900m));
            Assert.That(s.SaldoPrevistoOtimista, Is.EqualTo(1500m));
            Assert.That(s.SaldoPrevistoPiorCaso, Is.EqualTo(1050m));
            Assert.That(s.SaldoRealizado, Is.EqualTo(1200m));
            Assert.That(s.SaldoPrevisto, Is.EqualTo(s.SaldoPrevistoOtimista));
            Assert.That(s.SaldoReal, Is.EqualTo(s.SaldoRealizado));
        }
    }

    [Test]
    public async Task GetMonthAsync_MixedSplitsAndStatuses_ComputesBalancesAndCounters()
    {
        // Arrange — split spectrum: fully mine (paid), shared (paid + reimbursed), pass-through (open).
        GivenMonth(
            [
                Row(Bill(1, 1000m, split: 1m, actual: 1000m, paid: true)),
                Row(Bill(2, 800m, split: 0.5m, actual: 800m, paid: true, received: true)),
                Row(Bill(3, 500m, split: 0m)),
            ],
            Row(Income(1, 5000m, actual: 5200m, received: true)),
            Row(Income(2, 1000m)));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        var s = result.Value.Summary;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(s.PlannedExpense, Is.EqualTo(1400m));
            Assert.That(s.ActualExpense, Is.EqualTo(1400m));
            Assert.That(s.ReceivablePending, Is.EqualTo(500m));
            Assert.That(s.ReceivableReceived, Is.EqualTo(400m));
            Assert.That(s.PaidFull, Is.EqualTo(1800m));
            Assert.That(s.SaldoPrevistoOtimista, Is.EqualTo(4600m));
            Assert.That(s.SaldoPrevistoPiorCaso, Is.EqualTo(4100m));
            Assert.That(s.SaldoRealizado, Is.EqualTo(3800m));
            Assert.That(s.SaldoPrevistoOtimista - s.SaldoPrevistoPiorCaso, Is.EqualTo(s.ReceivablePending));
            Assert.That((s.BillsPaid, s.BillsTotal), Is.EqualTo((2, 3)));
            Assert.That((s.IncomesReceived, s.IncomesTotal), Is.EqualTo((1, 2)));
        }
    }

    [Test]
    public async Task GetMonthAsync_UnpaidAndUnreceived_CountOnlyTowardsPlanned()
    {
        // Arrange
        GivenMonth([Row(Bill(1, 300m))], Row(Income(1, 1000m)));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        var s = result.Value.Summary;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((s.PlannedExpense, s.ActualExpense), Is.EqualTo((300m, 0m)));
            Assert.That((s.PlannedIncome, s.ActualIncome), Is.EqualTo((1000m, 0m)));
            Assert.That(s.PaidFull, Is.Zero);
            Assert.That(s.SaldoPrevisto, Is.EqualTo(700m));
            Assert.That(s.SaldoReal, Is.Zero);
            Assert.That((s.BillsPaid, s.IncomesReceived), Is.EqualTo((0, 0)));
        }
    }

    [Test]
    public async Task GetMonthAsync_SeveralCategories_GroupsByCategoryOrderedByPlannedMyShareWithDiff()
    {
        // Arrange
        GivenMonth(
        [
            Row(Bill(1, 100m, actual: 120m, paid: true), LazerId),
            Row(Bill(2, 600m, split: 0.5m), MoradiaId),
            Row(Bill(3, 400m, actual: 380m, paid: true), MoradiaId),
        ]);

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        Assert.That(result.Value.ByCategory, Is.EqualTo(new[]
        {
            new DashboardCategoryDto(MoradiaId, "Moradia", 700m, 380m, -320m),
            new DashboardCategoryDto(LazerId, "Lazer", 100m, 120m, 20m),
        }));
    }

    [Test]
    public async Task GetMonthAsync_SameCategoryIdWithDifferentNames_GroupsByIdNotName()
    {
        // Arrange — grouping must be by category id; the name only labels the group.
        GivenMonth(
        [
            Row(Bill(1, 100m), MoradiaId, "Casa"),
            Row(Bill(2, 50m), LazerId, "Casa"),
        ]);

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        Assert.That(result.Value.ByCategory.Select(c => (c.CategoryId, c.Category, c.PlannedMyShare)),
            Is.EqualTo(new[] { (MoradiaId, "Casa", 100m), (LazerId, "Casa", 50m) }));
    }

    // --- GetYearAsync ---

    [TestCase(null)]
    [TestCase(1999)]
    [TestCase(2101)]
    public async Task GetYearAsync_InvalidYear_ReturnsValidationErrorWithoutQuerying(int? year)
    {
        // Act
        var result = await _sut.GetYearAsync(year, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, EntryValidation.YearField);
        await _billEntryRepository.DidNotReceiveWithAnyArgs().GetYearWithNamesAsync(default, default, default);
        await _incomeEntryRepository.DidNotReceiveWithAnyArgs().GetYearWithNameAsync(default, default, default);
    }

    [TestCase(2000)]
    [TestCase(2100)]
    public async Task GetYearAsync_BoundaryYear_Succeeds(int year)
    {
        // Act
        var result = await _sut.GetYearAsync(year, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        await _billEntryRepository.Received(1).GetYearWithNamesAsync(year, OwnerId, Arg.Any<CancellationToken>());
        await _incomeEntryRepository.Received(1).GetYearWithNameAsync(year, OwnerId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetYearAsync_NoEntries_ReturnsTwelveZeroedMonthsAndZeroTotals()
    {
        // Act
        var result = await _sut.GetYearAsync(2026, CancellationToken.None);

        // Assert
        var dto = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dto.Year, Is.EqualTo(2026));
            Assert.That(dto.Months, Is.EqualTo(Enumerable.Range(1, 12)
                .Select(m => new DashboardMonthSummaryDto(m, 0m, 0m, 0m, 0m, 0m, 0m))));
            Assert.That(dto.ByCategory, Is.Empty);
            Assert.That(dto.Totals, Is.EqualTo(new DashboardYearTotalsDto(0m, 0m, 0m, 0m, 0m, 0m)));
        }
    }

    [Test]
    public async Task GetYearAsync_EntriesInSomeMonths_FillsThoseMonthsAndZeroesTheRest()
    {
        // Arrange
        GivenYear(
            [
                Row(Bill(1, 100m, actual: 90m, paid: true, month: 1)),
                Row(Bill(2, 400m, split: 0.5m, month: 7)),
            ],
            Row(Income(1, 1000m, actual: 1100m, received: true, month: 1)),
            Row(Income(2, 500m, month: 7)));

        // Act
        var result = await _sut.GetYearAsync(2026, CancellationToken.None);

        // Assert
        var months = result.Value.Months;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(months.Select(m => m.Month), Is.EqualTo(Enumerable.Range(1, 12)));
            Assert.That(months[0], Is.EqualTo(new DashboardMonthSummaryDto(1, 100m, 90m, 1000m, 1100m, 900m, 1010m)));
            Assert.That(months[6], Is.EqualTo(new DashboardMonthSummaryDto(7, 200m, 0m, 500m, 0m, 300m, 0m)));
            Assert.That(months.Where(m => m.Month is not (1 or 7)),
                Has.All.Matches<DashboardMonthSummaryDto>(m => m == new DashboardMonthSummaryDto(m.Month, 0m, 0m, 0m, 0m, 0m, 0m)));
        }
    }

    [Test]
    public async Task GetYearAsync_EntriesAcrossMonths_ByCategorySumsWholeYearOrderedByPlannedMyShare()
    {
        // Arrange
        GivenYear(
        [
            Row(Bill(1, 50m, month: 2), LazerId),
            Row(Bill(2, 75m, actual: 80m, paid: true, month: 9), LazerId),
            Row(Bill(3, 1000m, split: 0.5m, month: 4), MoradiaId),
        ]);

        // Act
        var result = await _sut.GetYearAsync(2026, CancellationToken.None);

        // Assert
        Assert.That(result.Value.ByCategory, Is.EqualTo(new[]
        {
            new DashboardCategoryYearDto(MoradiaId, "Moradia", 500m, 0m),
            new DashboardCategoryYearDto(LazerId, "Lazer", 125m, 80m),
        }));
    }

    [Test]
    public async Task GetYearAsync_EntriesAcrossMonths_TotalsEqualSumOfTwelveMonths()
    {
        // Arrange
        GivenYear(
            [
                Row(Bill(1, 40m, actual: 45m, paid: true, month: 3)),
                Row(Bill(2, 60m, month: 11)),
            ],
            Row(Income(1, 300m, actual: 310m, received: true, month: 3)),
            Row(Income(2, 200m, month: 12)));

        // Act
        var result = await _sut.GetYearAsync(2026, CancellationToken.None);

        // Assert
        var dto = result.Value;
        Assert.That(dto.Totals, Is.EqualTo(new DashboardYearTotalsDto(
            dto.Months.Sum(m => m.PlannedExpense), dto.Months.Sum(m => m.ActualExpense),
            dto.Months.Sum(m => m.PlannedIncome), dto.Months.Sum(m => m.ActualIncome),
            dto.Months.Sum(m => m.SaldoPrevisto), dto.Months.Sum(m => m.SaldoReal))));
        Assert.That(dto.Totals, Is.EqualTo(new DashboardYearTotalsDto(100m, 45m, 500m, 310m, 400m, 265m)));
    }
}
