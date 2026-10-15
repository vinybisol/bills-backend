using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class ReceivablesEndpoints
{
    public static RouteGroupBuilder MapReceivablesEndpoints(this RouteGroupBuilder group)
    {
        var receivablesGroup = group
            .MapGroup("/receivables")
            .AddEndpointFilter<UserEndpointFilter>();

        receivablesGroup.MapGet("/month", GetReceivablesMonth);
        receivablesGroup.MapPost("/{entryId:long}/mark", MarkReceivable);
        receivablesGroup.MapPost("/{entryId:long}/unmark", UnmarkReceivable);
        receivablesGroup.MapPost("/mark-batch", MarkBatch);
        receivablesGroup.MapGet("/history", GetReceivablesHistory);
        return group;
    }

    private static async Task<IResult> GetReceivablesMonth(
        int? year,
        int? month,
        IReceivablesService receivablesService,
        CancellationToken ct)
    {
        var result = await receivablesService.GetMonthAsync(year, month, ct);

        return result.ToHttpResult();
    }

    // The body is optional: omitting it marks the entry as received now.
    private static async Task<IResult> MarkReceivable(
        long entryId,
        MarkReceivableRequest? req,
        IReceivablesService receivablesService,
        CancellationToken ct)
    {
        var result = await receivablesService.MarkAsync(entryId, req?.ReceivedDate, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> UnmarkReceivable(
        long entryId,
        IReceivablesService receivablesService,
        CancellationToken ct)
    {
        var result = await receivablesService.UnmarkAsync(entryId, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> MarkBatch(
        MarkBatchRequest req,
        IReceivablesService receivablesService,
        CancellationToken ct)
    {
        var result = await receivablesService.MarkBatchAsync(req.EntryIds, req.ReceivedDate, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> GetReceivablesHistory(
        long? personId,
        int? fromYear,
        int? fromMonth,
        int? toYear,
        int? toMonth,
        string? status,
        IReceivablesService receivablesService,
        CancellationToken ct)
    {
        var result = await receivablesService.GetHistoryAsync(personId, fromYear, fromMonth, toYear, toMonth, status, ct);

        return result.ToHttpResult();
    }
}
