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
public sealed class ReceivablesServiceTests
{
    private const long OwnerId = 7L;
    private const long BillId = 10L;
    private const long PersonId = 30L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IBillEntryRepository _repository = null!;
    private IPersonRepository _personRepository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ReceivablesService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IBillEntryRepository>();
        _personRepository = Substitute.For<IPersonRepository>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new ReceivablesService(_repository, _personRepository, _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private static BillEntry Entry(
        long id, decimal planned = 100m, decimal split = 0.5m, long? personId = PersonId,
        int year = 2026, int month = 3, bool paid = false, bool received = false)
    {
        var entry = EntityId.With(BillEntry.Create(OwnerId, BillId, year, month, planned, split, personId, FixedNow), id);
        if (paid)
            entry.MarkPaid(FixedNow.AddDays(-3));
        if (received)
            entry.MarkReceived(FixedNow.AddDays(-1));
        return entry;
    }

    private BillEntry GivenEntry(long id, decimal split = 0.5m, long? personId = PersonId, bool paid = false, bool received = false)
    {
        var entry = Entry(id, split: split, personId: personId, paid: paid, received: received);
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(entry);
        return entry;
    }

    private void GivenMonthRows(params BillEntryWithNamesDto[] rows) =>
        _repository.GetMonthWithNamesAsync(2026, 3, OwnerId, Arg.Any<CancellationToken>()).Returns(rows);

    private void GivenPerson(string name = "Esposa")
    {
        var person = EntityId.With(Person.Create(OwnerId, name, FixedNow), PersonId);
        _personRepository.GetByIdAsync(PersonId, Arg.Any<CancellationToken>()).Returns(person);
    }

    private void GivenHistoryRows(params BillEntryWithNamesDto[] rows) =>
        _repository.GetReceivablesByPersonWithNamesAsync(PersonId, OwnerId, Arg.Any<CancellationToken>()).Returns(rows);

    private static BillEntryWithNamesDto Row(BillEntry entry, string name = "Aluguel", string? person = "Esposa") =>
        new(entry, name, 1L, "Moradia", person);

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

    private static DateTimeOffset MidnightUtc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    // --- GetMonthAsync ---

    [TestCase(null, 3, EntryValidation.YearField)]
    [TestCase(1999, 3, EntryValidation.YearField)]
    [TestCase(2026, null, EntryValidation.MonthField)]
    [TestCase(2026, 0, EntryValidation.MonthField)]
    [TestCase(2026, 13, EntryValidation.MonthField)]
    public async Task GetMonthAsync_InvalidPeriod_ReturnsValidationErrorWithoutQuerying(int? year, int? month, string field)
    {
        // Act
        var result = await _sut.GetMonthAsync(year, month, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, field);
        await _repository.DidNotReceiveWithAnyArgs().GetMonthWithNamesAsync(default, default, default, default);
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
    public async Task GetMonthAsync_NoEntries_ReturnsEmptyPanelWithZeroTotal()
    {
        // Arrange
        GivenMonthRows();

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That((result.Value.Year, result.Value.Month), Is.EqualTo((2026, 3)));
            Assert.That(result.Value.ByPerson, Is.Empty);
            Assert.That(result.Value.TotalPendenteGeral, Is.Zero);
        }
    }

    [Test]
    public async Task GetMonthAsync_MixedEntries_GroupsReceivablesByPersonAndExcludesNonReceivables()
    {
        // Arrange
        GivenMonthRows(
            Row(Entry(3, planned: 100m), "Telefone"),
            Row(Entry(1, planned: 1000m, received: true), "Aluguel"),
            Row(Entry(2, planned: 50m, split: 1m, personId: null), "Netflix", null),
            Row(Entry(4, planned: 200m, split: 0m, personId: 31L), "Luz", "Ana"));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        var panel = result.Value;
        Assert.That(panel.ByPerson.Select(p => p.Name), Is.EqualTo(new[] { "Ana", "Esposa" }));
        var ana = panel.ByPerson[0];
        var esposa = panel.ByPerson[1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That((ana.PersonId, ana.TotalDevido, ana.JaRecebido, ana.Pendente), Is.EqualTo((31L, 200m, 0m, 200m)));
            Assert.That(ana.Items, Is.EqualTo(new[] { new ReceivableItemDto(4, "Luz", 200m, false) }));
            Assert.That(esposa.PersonId, Is.EqualTo(PersonId));
            Assert.That((esposa.TotalDevido, esposa.JaRecebido, esposa.Pendente), Is.EqualTo((550m, 500m, 50m)));
            Assert.That(esposa.Items, Is.EqualTo(new[]
            {
                new ReceivableItemDto(1, "Aluguel", 500m, true),
                new ReceivableItemDto(3, "Telefone", 50m, false),
            }));
            Assert.That(panel.TotalPendenteGeral, Is.EqualTo(250m));
        }
    }

    [Test]
    public async Task GetMonthAsync_PersonNameUnresolved_FallsBackToEmptyName()
    {
        // Arrange
        GivenMonthRows(Row(Entry(1), person: null));

        // Act
        var result = await _sut.GetMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        Assert.That(result.Value.ByPerson.Single().Name, Is.Empty);
    }

    // --- MarkAsync ---

    [Test]
    public async Task MarkAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.MarkAsync(99, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task MarkAsync_NonReceivableEntry_ReturnsValidationErrorAndKeepsEntry()
    {
        // Arrange
        var entry = GivenEntry(1, split: 1m, personId: null);

        // Act
        var result = await _sut.MarkAsync(1, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, ReceivablesService.EntryIdField);
        Assert.That(entry.Received, Is.False);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task MarkAsync_WithoutDate_MarksReceivedNowAndSaves()
    {
        // Arrange
        GivenEntry(1);

        // Act
        var result = await _sut.MarkAsync(1, null, CancellationToken.None);

        // Assert
        Assert.That((result.Value.Received, result.Value.ReceivedDate), Is.EqualTo((true, (DateTimeOffset?)FixedNow)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsync_WithDate_RecordsMidnightUtc()
    {
        // Arrange
        GivenEntry(1);

        // Act
        var result = await _sut.MarkAsync(1, new DateOnly(2026, 3, 10), CancellationToken.None);

        // Assert
        Assert.That(result.Value.ReceivedDate, Is.EqualTo(MidnightUtc(2026, 3, 10)));
    }

    [Test]
    public async Task MarkAsync_AlreadyReceived_IsIdempotentAndReappliesDate()
    {
        // Arrange
        GivenEntry(1, received: true);

        // Act
        var result = await _sut.MarkAsync(1, new DateOnly(2026, 4, 1), CancellationToken.None);

        // Assert
        Assert.That((result.Value.Received, result.Value.ReceivedDate), Is.EqualTo((true, (DateTimeOffset?)MidnightUtc(2026, 4, 1))));
    }

    [Test]
    public async Task MarkAsync_PaidEntry_KeepsPaidUntouched()
    {
        // Arrange
        var entry = GivenEntry(1, paid: true);
        var paidDate = entry.PaidDate;

        // Act
        var result = await _sut.MarkAsync(1, null, CancellationToken.None);

        // Assert
        Assert.That((result.Value.Paid, result.Value.PaidDate, result.Value.Received), Is.EqualTo((true, paidDate, true)));
    }

    // --- UnmarkAsync ---

    [Test]
    public async Task UnmarkAsync_EntryNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _sut.UnmarkAsync(99, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task UnmarkAsync_ReceivedPaidEntry_ClearsReceivedAndKeepsPaid()
    {
        // Arrange
        var entry = GivenEntry(1, paid: true, received: true);
        var paidDate = entry.PaidDate;

        // Act
        var result = await _sut.UnmarkAsync(1, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.Received, result.Value.ReceivedDate), Is.EqualTo((false, (DateTimeOffset?)null)));
            Assert.That((result.Value.Paid, result.Value.PaidDate), Is.EqualTo((true, paidDate)));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnmarkAsync_NotReceived_IsIdempotent()
    {
        // Arrange
        GivenEntry(1);

        // Act
        var result = await _sut.UnmarkAsync(1, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess && !result.Value.Received, Is.True);
    }

    // --- MarkBatchAsync ---

    [Test]
    public async Task MarkBatchAsync_NullIds_ReturnsValidationErrorWithoutQuerying()
    {
        // Act
        var result = await _sut.MarkBatchAsync(null, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, ReceivablesService.EntryIdsField);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdsAsync(default!, default);
    }

    [Test]
    public async Task MarkBatchAsync_EmptyIds_ReturnsValidationError()
    {
        // Act
        var result = await _sut.MarkBatchAsync([], null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, ReceivablesService.EntryIdsField);
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task MarkBatchAsync_SomeIdsMissingOrForeign_ReturnsNotFoundAndMarksNothing()
    {
        // Arrange
        var found = Entry(1);
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns([found]);

        // Act
        var result = await _sut.MarkBatchAsync([1, 5, 2], null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error.Message, Does.Contain("2, 5"));
            Assert.That(found.Received, Is.False);
        }
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task MarkBatchAsync_SomeNonReceivable_ReturnsValidationErrorAndMarksNothing()
    {
        // Arrange
        var receivable = Entry(1);
        var notReceivable = Entry(2, split: 1m, personId: null);
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns([receivable, notReceivable]);

        // Act
        var result = await _sut.MarkBatchAsync([1, 2], null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, ReceivablesService.EntryIdsField);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((ValidationError)result.Error).Errors[0].Message, Does.Contain("2"));
            Assert.That(receivable.Received, Is.False);
            Assert.That(notReceivable.Received, Is.False);
        }
        await AssertNotSavedAsync();
    }

    [Test]
    public async Task MarkBatchAsync_AllReceivable_MarksAllOnceAndReturnsDistinctCount()
    {
        // Arrange
        var a = Entry(1);
        var b = Entry(2, received: true);
        IReadOnlyCollection<long>? queried = null;
        _repository.GetByIdsAsync(Arg.Do<IReadOnlyCollection<long>>(ids => queried = ids), Arg.Any<CancellationToken>()).Returns([a, b]);

        // Act
        var result = await _sut.MarkBatchAsync([1, 2, 1], new DateOnly(2026, 3, 15), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(new MarkBatchResultDto(2)));
            Assert.That(queried, Is.EquivalentTo(new long[] { 1, 2 }));
            Assert.That((a.Received, a.ReceivedDate), Is.EqualTo((true, (DateTimeOffset?)MidnightUtc(2026, 3, 15))));
            Assert.That((b.Received, b.ReceivedDate), Is.EqualTo((true, (DateTimeOffset?)MidnightUtc(2026, 3, 15))));
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkBatchAsync_WithoutDate_UsesNow()
    {
        // Arrange
        var a = Entry(1);
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns([a]);

        // Act
        await _sut.MarkBatchAsync([1], null, CancellationToken.None);

        // Assert
        Assert.That(a.ReceivedDate, Is.EqualTo(FixedNow));
    }

    // --- GetHistoryAsync ---

    [Test]
    public async Task GetHistoryAsync_NullPersonId_ReturnsValidationErrorWithoutQuerying()
    {
        // Act
        var result = await _sut.GetHistoryAsync(null, null, null, null, null, null, CancellationToken.None);

        // Assert
        AssertValidationFailure(result, ReceivablesService.PersonIdField);
        await _personRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task GetHistoryAsync_PersonNotFound_ReturnsNotFoundWithoutQueryingEntries()
    {
        // Act
        var result = await _sut.GetHistoryAsync(PersonId, null, null, null, null, null, CancellationToken.None);

        // Assert
        AssertFailure(result, ErrorType.NotFound);
        Assert.That(result.Error.Message, Does.StartWith(ReceivablesService.PersonEntity));
        await _repository.DidNotReceiveWithAnyArgs().GetReceivablesByPersonWithNamesAsync(default, default, default);
    }

    [Test]
    public async Task GetHistoryAsync_NoFilters_ReturnsAllItemsMostRecentFirstWithTotals()
    {
        // Arrange
        GivenPerson("Esposa");
        GivenHistoryRows(
            Row(Entry(1, planned: 1000m, year: 2026, month: 1, received: true), "Aluguel"),
            Row(Entry(3, planned: 100m, year: 2026, month: 2), "Telefone"),
            Row(Entry(2, planned: 1000m, year: 2026, month: 2), "Aluguel"),
            Row(Entry(4, planned: 40m, year: 2025, month: 12), "Luz"));

        // Act
        var result = await _sut.GetHistoryAsync(PersonId, null, null, null, null, null, CancellationToken.None);

        // Assert
        var history = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((history.PersonId, history.Name), Is.EqualTo((PersonId, "Esposa")));
            Assert.That(history.Items.Select(i => i.EntryId), Is.EqualTo(new long[] { 2, 3, 1, 4 }));
            Assert.That(history.Items[0], Is.EqualTo(new ReceivablesHistoryItemDto(2, "Aluguel", 2026, 2, 500m, false, null)));
            Assert.That(history.Items[2].ReceivedDate, Is.EqualTo(FixedNow.AddDays(-1)));
            Assert.That(history.Totals, Is.EqualTo(new ReceivablesHistoryTotalsDto(1070m, 500m, 570m)));
        }
    }

    [Test]
    public async Task GetHistoryAsync_PeriodFilter_NarrowsItemsAndTotals()
    {
        // Arrange
        GivenPerson();
        GivenHistoryRows(
            Row(Entry(1, year: 2026, month: 1)),
            Row(Entry(2, year: 2026, month: 5)),
            Row(Entry(3, year: 2026, month: 9)));

        // Act
        var result = await _sut.GetHistoryAsync(PersonId, 2026, 3, 2026, 6, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Items.Select(i => i.EntryId), Is.EqualTo(new long[] { 2 }));
            Assert.That(result.Value.Totals.TotalDevido, Is.EqualTo(50m));
        }
    }

    [TestCase("received", new long[] { 1 })]
    [TestCase("pending", new long[] { 2 })]
    [TestCase("whatever", new long[] { 2, 1 })]
    [TestCase(null, new long[] { 2, 1 })]
    public async Task GetHistoryAsync_StatusFilter_KeepsMatchingItems(string? status, long[] expectedIds)
    {
        // Arrange
        GivenPerson();
        GivenHistoryRows(
            Row(Entry(1, month: 1, received: true)),
            Row(Entry(2, month: 2)));

        // Act
        var result = await _sut.GetHistoryAsync(PersonId, null, null, null, null, status, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Items.Select(i => i.EntryId), Is.EqualTo(expectedIds));
    }

    [Test]
    public async Task GetHistoryAsync_NoEntries_ReturnsEmptyItemsAndZeroTotals()
    {
        // Arrange
        GivenPerson();
        GivenHistoryRows();

        // Act
        var result = await _sut.GetHistoryAsync(PersonId, null, null, null, null, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Items, Is.Empty);
            Assert.That(result.Value.Totals, Is.EqualTo(new ReceivablesHistoryTotalsDto(0m, 0m, 0m)));
        }
    }
}
