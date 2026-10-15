namespace Api.Contracts;

/// <summary>The (optional) request body for <c>POST /api/v1/receivables/{entryId}/mark</c>.</summary>
/// <param name="ReceivedDate">The date the split was received (stored as midnight UTC), or <see langword="null"/> to use the current instant.</param>
internal sealed record MarkReceivableRequest(DateOnly? ReceivedDate);

/// <summary>The request body for <c>POST /api/v1/receivables/mark-batch</c>.</summary>
/// <param name="EntryIds">The bill entry ids to mark as received; must all be receivables owned by the caller.</param>
/// <param name="ReceivedDate">The date the split was received (stored as midnight UTC), or <see langword="null"/> to use the current instant.</param>
internal sealed record MarkBatchRequest(IReadOnlyList<long>? EntryIds, DateOnly? ReceivedDate);
