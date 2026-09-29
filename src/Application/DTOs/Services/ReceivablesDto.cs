namespace Application.DTOs.Services;

/// <summary>A single bill entry row within a person's panel in <c>GET /api/v1/receivables/month</c>.</summary>
/// <param name="EntryId">The bill entry id.</param>
/// <param name="Bill">The bill's display name (resolved even if the bill template was since deactivated).</param>
/// <param name="Receivable">The amount owed to the owner: effective amount × (1 − split ratio).</param>
/// <param name="Received">Whether this split portion has already been received.</param>
public sealed record ReceivableItemDto(long EntryId, string Bill, decimal Receivable, bool Received);

/// <summary>A single person's row in the <c>GET /api/v1/receivables/month</c> panel.</summary>
/// <param name="PersonId">The person id.</param>
/// <param name="Name">The person's display name (resolved even if the person was since deactivated).</param>
/// <param name="TotalDevido">Sum of the receivable amount across all of this person's entries in the month.</param>
/// <param name="JaRecebido">Sum of the receivable amount across entries already marked received.</param>
/// <param name="Pendente">Sum of the receivable amount across entries not yet received.</param>
/// <param name="Items">The individual bill entries owed by this person, ordered by entry id.</param>
public sealed record PersonReceivablesDto(
    long PersonId, string Name, decimal TotalDevido, decimal JaRecebido, decimal Pendente,
    IReadOnlyList<ReceivableItemDto> Items);

/// <summary>The complete response of <c>GET /api/v1/receivables/month</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="ByPerson">One row per person with at least one receivable entry in the month, ordered by name.</param>
/// <param name="TotalPendenteGeral">Sum of <see cref="PersonReceivablesDto.Pendente"/> across all people.</param>
public sealed record ReceivablesMonthDto(
    int Year, int Month, IReadOnlyList<PersonReceivablesDto> ByPerson, decimal TotalPendenteGeral);

/// <summary>The response of <c>POST /api/v1/receivables/mark-batch</c>.</summary>
/// <param name="Marked">The number of (distinct) entries marked as received.</param>
public sealed record MarkBatchResultDto(int Marked);

/// <summary>A single item row of <c>GET /api/v1/receivables/history</c>.</summary>
/// <param name="EntryId">The bill entry id.</param>
/// <param name="Bill">The bill's display name (resolved even if the bill template was since deactivated).</param>
/// <param name="Year">The entry's reference year.</param>
/// <param name="Month">The entry's reference month (1–12).</param>
/// <param name="Receivable">The amount owed to the owner: effective amount × (1 − split ratio).</param>
/// <param name="Received">Whether this split portion has already been received.</param>
/// <param name="ReceivedDate">The UTC instant the split was received, or <see langword="null"/>.</param>
public sealed record ReceivablesHistoryItemDto(
    long EntryId, string Bill, int Year, int Month, decimal Receivable, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>Aggregated totals of <c>GET /api/v1/receivables/history</c>, computed over the filtered slice.</summary>
/// <param name="TotalDevido">Sum of the receivable amount across the filtered items.</param>
/// <param name="TotalRecebido">Sum of the receivable amount across received items only.</param>
/// <param name="TotalPendente">Sum of the receivable amount across pending items only.</param>
public sealed record ReceivablesHistoryTotalsDto(decimal TotalDevido, decimal TotalRecebido, decimal TotalPendente);

/// <summary>The complete response of <c>GET /api/v1/receivables/history</c>.</summary>
/// <param name="PersonId">The requested person's id.</param>
/// <param name="Name">The person's display name.</param>
/// <param name="Totals">Aggregates computed over whatever period/status filter was applied.</param>
/// <param name="Items">Item-level rows, most recent first (year, month descending; then entry id).</param>
public sealed record ReceivablesHistoryDto(
    long PersonId, string Name, ReceivablesHistoryTotalsDto Totals, IReadOnlyList<ReceivablesHistoryItemDto> Items);
