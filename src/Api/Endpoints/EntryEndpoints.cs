using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class EntryEndpoints
{
    public static RouteGroupBuilder MapEntryEndpoints(this RouteGroupBuilder group)
    {
        var entryGroup = group
            .MapGroup("/entries")
            .AddEndpointFilter<UserEndpointFilter>();

        entryGroup.MapGet("", GetEntries);
        entryGroup.MapPost("/bill", CreateBillEntry);
        entryGroup.MapPost("/income", CreateIncomeEntry);
        entryGroup.MapDelete("/bill/{id:long}", DeleteBillEntry);
        entryGroup.MapDelete("/income/{id:long}", DeleteIncomeEntry);
        entryGroup.MapPatch("/bill/{id:long}", PatchBillEntry);
        entryGroup.MapPost("/bill/{id:long}/pay", PayBillEntry);
        entryGroup.MapPost("/bill/{id:long}/unpay", UnpayBillEntry);
        entryGroup.MapPatch("/income/{id:long}", PatchIncomeEntry);
        entryGroup.MapPost("/income/{id:long}/receive", ReceiveIncomeEntry);
        entryGroup.MapPost("/income/{id:long}/unreceive", UnreceiveIncomeEntry);
        return group;
    }

    private static async Task<IResult> GetEntries(
        int? year,
        int? month,
        IEntryService entryService,
        CancellationToken ct)
    {
        var result = await entryService.GetMonthAsync(year, month, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateBillEntry(
        HttpRequest httpRequest,
        CreateBillEntryRequest req,
        IBillEntryService billEntryService,
        CancellationToken ct)
    {
        var result = await billEntryService.CreateAsync(req.BillId, req.Year, req.Month, req.PlannedAmount, ct);

        if (result.IsFailure)
            return result.ToHttpResult();

        var entry = result.Value;
        return Results.Created($"{httpRequest.Path.Value}/{entry.Id}", entry);
    }

    private static async Task<IResult> CreateIncomeEntry(
        HttpRequest httpRequest,
        CreateIncomeEntryRequest req,
        IIncomeEntryService incomeEntryService,
        CancellationToken ct)
    {
        var result = await incomeEntryService.CreateAsync(req.IncomeId, req.Year, req.Month, req.PlannedAmount, ct);

        if (result.IsFailure)
            return result.ToHttpResult();

        var entry = result.Value;
        return Results.Created($"{httpRequest.Path.Value}/{entry.Id}", entry);
    }

    private static async Task<IResult> DeleteBillEntry(
        long id,
        IBillEntryService billEntryService,
        CancellationToken ct)
    {
        var result = await billEntryService.DeleteByIdAsync(id, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteIncomeEntry(
        long id,
        IIncomeEntryService incomeEntryService,
        CancellationToken ct)
    {
        var result = await incomeEntryService.DeleteByIdAsync(id, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> PatchBillEntry(
        long id,
        PatchBillEntryRequest req,
        IBillEntryService billEntryService,
        CancellationToken ct)
    {
        var result = await billEntryService.UpdateAmountsAsync(id, req.PlannedAmount, req.ActualAmount, ct);

        return result.ToHttpResult();
    }

    // The body is optional: omitting it pays the planned amount now.
    private static async Task<IResult> PayBillEntry(
        long id,
        PayBillEntryRequest? req,
        IBillEntryService billEntryService,
        CancellationToken ct)
    {
        var result = await billEntryService.PayAsync(id, req?.ActualAmount, req?.PaidDate, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> UnpayBillEntry(
        long id,
        IBillEntryService billEntryService,
        CancellationToken ct)
    {
        var result = await billEntryService.UnpayAsync(id, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> PatchIncomeEntry(
        long id,
        PatchIncomeEntryRequest req,
        IIncomeEntryService incomeEntryService,
        CancellationToken ct)
    {
        var result = await incomeEntryService.UpdateAmountsAsync(id, req.PlannedAmount, req.ActualAmount, ct);

        return result.ToHttpResult();
    }

    // The body is optional: omitting it receives the planned amount now.
    private static async Task<IResult> ReceiveIncomeEntry(
        long id,
        ReceiveIncomeEntryRequest? req,
        IIncomeEntryService incomeEntryService,
        CancellationToken ct)
    {
        var result = await incomeEntryService.ReceiveAsync(id, req?.ActualAmount, req?.ReceivedDate, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> UnreceiveIncomeEntry(
        long id,
        IIncomeEntryService incomeEntryService,
        CancellationToken ct)
    {
        var result = await incomeEntryService.UnreceiveAsync(id, ct);

        return result.ToHttpResult();
    }
}
