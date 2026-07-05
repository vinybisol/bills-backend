namespace BillsBackend.Api.Contracts;

/// <summary>A single bill entry row within a person's panel in <c>GET /api/receivables/month</c>.</summary>
/// <param name="EntryId">The bill entry id.</param>
/// <param name="Bill">The bill's display name (resolved even if the bill template was since deactivated).</param>
/// <param name="Receivable">The amount owed to the owner: effective amount × (1 − split ratio).</param>
/// <param name="Received">Whether this split portion has already been received.</param>
internal sealed record ReceivableItemDto(long EntryId, string Bill, decimal Receivable, bool Received);

/// <summary>A single person's row in the <c>GET /api/receivables/month</c> panel.</summary>
/// <param name="PersonId">The person id.</param>
/// <param name="Name">The person's display name.</param>
/// <param name="TotalDevido">Sum of <see cref="Receivable"/> across all of this person's entries in the month.</param>
/// <param name="JaRecebido">Sum of <see cref="Receivable"/> across entries already marked received.</param>
/// <param name="Pendente">Sum of <see cref="Receivable"/> across entries not yet received.</param>
/// <param name="Items">The individual bill entries owed by this person.</param>
internal sealed record PersonReceivablesDto(
    long PersonId, string Name, decimal TotalDevido, decimal JaRecebido, decimal Pendente,
    IReadOnlyList<ReceivableItemDto> Items);

/// <summary>The complete response returned by <c>GET /api/receivables/month</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="ByPerson">One row per person with at least one receivable entry in the month, ordered by name.</param>
/// <param name="TotalPendenteGeral">Sum of <see cref="PersonReceivablesDto.Pendente"/> across all people.</param>
internal sealed record ReceivablesMonthDto(
    int Year, int Month, IReadOnlyList<PersonReceivablesDto> ByPerson, decimal TotalPendenteGeral);

/// <summary>The request body for <c>POST /api/receivables/{entryId}/mark</c>.</summary>
/// <param name="ReceivedDate">The date the split was received, or <see langword="null"/> to use the current instant.</param>
internal sealed record MarkReceivableRequest(DateOnly? ReceivedDate);

/// <summary>The request body for <c>POST /api/receivables/mark-batch</c>.</summary>
/// <param name="EntryIds">The bill entry ids to mark as received; must all be valid receivables owned by the caller.</param>
/// <param name="ReceivedDate">The date the split was received, or <see langword="null"/> to use the current instant.</param>
internal sealed record MarkBatchRequest(IReadOnlyList<long> EntryIds, DateOnly? ReceivedDate);

/// <summary>The response returned by <c>POST /api/receivables/mark-batch</c>.</summary>
/// <param name="Marked">The number of entries marked as received.</param>
internal sealed record MarkBatchResponse(int Marked);

/// <summary>A single item row returned by <c>GET /api/receivables/history</c>.</summary>
/// <param name="EntryId">The bill entry id.</param>
/// <param name="Bill">The bill's display name (resolved even if the bill template was since deactivated).</param>
/// <param name="Year">The entry's reference year.</param>
/// <param name="Month">The entry's reference month (1–12).</param>
/// <param name="Receivable">The amount owed to the owner: effective amount × (1 − split ratio).</param>
/// <param name="Received">Whether this split portion has already been received.</param>
/// <param name="ReceivedDate">The UTC instant the split was received, or <see langword="null"/>.</param>
internal sealed record ReceivablesHistoryItemDto(
    long EntryId, string Bill, int Year, int Month, decimal Receivable, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>Aggregated totals returned by <c>GET /api/receivables/history</c>, computed over the filtered slice.</summary>
/// <param name="TotalDevido">Sum of <see cref="ReceivablesHistoryItemDto.Receivable"/> across the filtered items.</param>
/// <param name="TotalRecebido">Sum of <see cref="ReceivablesHistoryItemDto.Receivable"/> across received items only.</param>
/// <param name="TotalPendente">Sum of <see cref="ReceivablesHistoryItemDto.Receivable"/> across pending items only.</param>
internal sealed record ReceivablesHistoryTotalsDto(decimal TotalDevido, decimal TotalRecebido, decimal TotalPendente);

/// <summary>The complete response returned by <c>GET /api/receivables/history</c>.</summary>
/// <param name="PersonId">The requested person's id.</param>
/// <param name="Name">The person's display name.</param>
/// <param name="Totals">Aggregates computed over whatever period/status filter was applied.</param>
/// <param name="Items">Item-level rows, ordered by year then month descending (most recent first).</param>
internal sealed record ReceivablesHistoryDto(
    long PersonId, string Name, ReceivablesHistoryTotalsDto Totals, IReadOnlyList<ReceivablesHistoryItemDto> Items);
