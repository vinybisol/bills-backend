using Application.Abstractions.Repositories;
using Application.Abstractions.Services;
using Application.DTOs.Services;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Calculations;

namespace Application.Services;

internal sealed class ReceivablesService(
    IBillEntryRepository billEntryRepository,
    IPersonRepository personRepository,
    ICurrentOwner currentOwner,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IReceivablesService
{
    internal const string EntryIdField = "entryId";
    internal const string EntryIdsField = "entryIds";
    internal const string PersonIdField = "personId";

    internal const string PersonEntity = "Pessoa";

    public async Task<Result<ReceivablesMonthDto>> GetMonthAsync(int? year, int? month, CancellationToken ct)
        => await GetMonthBillsAsync(year, month, null, ct);
    public async Task<Result<ReceivablesMonthDto>> GetMonthByPersonAsync(int? year, int? month, long personId, CancellationToken ct)
        => await GetMonthBillsAsync(year, month, personId, ct);

    private async Task<Result<ReceivablesMonthDto>> GetMonthBillsAsync(int? year, int? month, long? personId, CancellationToken ct)
    {
        List<Error> errors = [];
        EntryValidation.AddPeriodErrors(errors, year, month);
        var validation = EntryValidation.ToValidationError(errors);

        if (validation is not null)
            return validation;

        IReadOnlyCollection<BillEntryWithNamesDto> rows = [];

        if (personId is null || personId <= 0)
            rows = await billEntryRepository.GetMonthWithNamesAsync(year!.Value, month!.Value, currentOwner.Id, ct);
        else
            rows = await billEntryRepository.GetMonthByPersonIdWithNamesAsync(year!.Value, month!.Value, currentOwner.Id, personId.Value, ct);

        var byPerson = rows
            .Where(r => EntryAggregations.IsReceivable(r.Entry))
            .GroupBy(r => r.Entry.PersonId!.Value)
            .Select(g =>
            {
                var items = g
                    .OrderBy(r => r.Entry.Id)
                    .Select(r => new ReceivableItemDto(
                        r.Entry.Id, r.Name, EntryAggregations.EffectiveReceivable(r.Entry), r.Entry.Received))
                    .ToList();
                var totals = EntryAggregations.SummarizeReceivables(g.Select(r => r.Entry));

                return new PersonReceivablesDto(
                    g.Key, g.First().Person ?? string.Empty, totals.Total, totals.Received, totals.Pending, items);
            })
            .OrderBy(p => p.Name)
            .ThenBy(p => p.PersonId)
            .ToList();

        return new ReceivablesMonthDto(year.Value, month.Value, byPerson, byPerson.Sum(p => p.Pendente));
    }

    // Idempotent: marking an already-received entry re-applies the received date. Never touches
    // paid/paidDate, which track the independent fact that the owner paid the bill.
    public async Task<Result<BillEntryDto>> MarkAsync(long entryId, DateOnly? receivedDate, CancellationToken ct)
    {
        var entry = await billEntryRepository.GetByIdAsync(entryId, ct);
        if (entry is null)
            return Error.NotFound(BillEntryService.BillEntryEntity);

        if (!EntryAggregations.IsReceivable(entry))
            return Invalid(EntryIdField, $"Entry {entryId} has no split; it is not a receivable.");

        entry.MarkReceived(EntryCalculations.ResolveEventInstant(receivedDate, timeProvider.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(ct);

        return BillEntryDto.From(entry);
    }

    // Idempotent; never touches paid/paidDate.
    public async Task<Result<BillEntryDto>> UnmarkAsync(long entryId, CancellationToken ct)
    {
        var entry = await billEntryRepository.GetByIdAsync(entryId, ct);
        if (entry is null)
            return Error.NotFound(BillEntryService.BillEntryEntity);

        entry.UnmarkReceived();
        await unitOfWork.SaveChangesAsync(ct);

        return BillEntryDto.From(entry);
    }

    // All-or-nothing: every (distinct) id must exist, belong to the caller and be a receivable;
    // otherwise nothing is marked. A single SaveChanges makes the write atomic.
    public async Task<Result<MarkBatchResultDto>> MarkBatchAsync(IReadOnlyCollection<long>? entryIds, DateOnly? receivedDate, CancellationToken ct)
    {
        if (entryIds is null || entryIds.Count == 0)
            return Invalid(EntryIdsField, "EntryIds must contain at least one id.");

        var ids = entryIds.ToHashSet();
        var entries = await billEntryRepository.GetByIdsAsync(ids, ct);

        // Unknown and foreign ids are indistinguishable: the owner filter hides the latter.
        var missing = ids.Except(entries.Select(e => e.Id)).Order().ToList();
        if (missing.Count > 0)
            return new Error("Error.NotFound", $"{BillEntryService.BillEntryEntity} não encontrado: {string.Join(", ", missing)}.", ErrorType.NotFound);

        var notReceivable = entries.Where(e => !EntryAggregations.IsReceivable(e)).Select(e => e.Id).Order().ToList();
        if (notReceivable.Count > 0)
            return Invalid(EntryIdsField, $"Entries {string.Join(", ", notReceivable)} have no split; they are not receivables.");

        var receivedAt = EntryCalculations.ResolveEventInstant(receivedDate, timeProvider.GetUtcNow());
        foreach (var entry in entries)
            entry.MarkReceived(receivedAt);

        await unitOfWork.SaveChangesAsync(ct);

        return new MarkBatchResultDto(entries.Count);
    }

    // Unrecognized/missing status means "all". The person must be active and the caller's own
    // (owner-filtered lookup): unknown, foreign and deactivated people are all 404.
    public async Task<Result<ReceivablesHistoryDto>> GetHistoryAsync(
        long? personId, int? fromYear, int? fromMonth, int? toYear, int? toMonth, string? status, CancellationToken ct)
    {
        if (personId is null)
            return Invalid(PersonIdField, "PersonId is required.");

        var person = await personRepository.GetByIdAsync(personId.Value, ct);
        if (person is null)
            return Error.NotFound(PersonEntity);

        var rows = await billEntryRepository.GetReceivablesByPersonWithNamesAsync(person.Id, currentOwner.Id, ct);
        var namesById = rows.ToDictionary(r => r.Entry.Id, r => r.Name);

        var entries = EntryAggregations
            .FilterByReceivedStatus(
                rows.Select(r => r.Entry)
                    .Where(e => EntryCalculations.IsInPeriod(e.RefYear, e.RefMonth, fromYear, fromMonth, toYear, toMonth)),
                status)
            .ToList();

        var items = entries
            .OrderByDescending(e => e.RefYear)
            .ThenByDescending(e => e.RefMonth)
            .ThenBy(e => e.Id)
            .Select(e => new ReceivablesHistoryItemDto(
                e.Id, namesById[e.Id], e.RefYear, e.RefMonth,
                EntryAggregations.EffectiveReceivable(e), e.Received, e.ReceivedDate))
            .ToList();

        var totals = EntryAggregations.SummarizeReceivables(entries);

        return new ReceivablesHistoryDto(
            person.Id, person.Name,
            new ReceivablesHistoryTotalsDto(totals.Total, totals.Received, totals.Pending),
            items);
    }

    private static ValidationError Invalid(string field, string message) =>
        new([new Error(field, message, ErrorType.Validation)]);
}
