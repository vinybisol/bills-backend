using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class BillEndpoints
{
    public static RouteGroupBuilder MapBillEndpoints(this RouteGroupBuilder group)
    {
        var billGroup = group
            .MapGroup("/bills")
            .AddEndpointFilter<UserEndpointFilter>();

        billGroup.MapPost("", CreateBill);
        billGroup.MapGet("", ListBills);
        billGroup.MapPut("/{id:long}", UpdateBill);
        billGroup.MapDelete("/{id:long}", DeleteBill);
        billGroup.MapPost("/{billId:long}/recalculate", RecalculateBill);
        billGroup.MapGet("/{billId:long}/history", GetBillHistory);
        return group;
    }

    private static async Task<IResult> CreateBill(
        HttpRequest httpRequest,
        CreateBillRequest req,
        IBillService billService,
        CancellationToken ct)
    {
        var result = await billService.CreateAsync(
            req.Name, req.CategoryId, req.Kind, req.DefaultAmount, req.SplitRatio, req.PersonId, ct);

        if (result.IsFailure)
            return result.ToHttpResult();

        var bill = result.Value;
        return Results.Created($"{httpRequest.Path.Value}/{bill.Id}", bill);
    }

    private static async Task<IResult> ListBills(
        IBillService billService,
        CancellationToken ct)
    {
        var result = await billService.GetAllByNameAsync(ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateBill(
        long id,
        UpdateBillRequest req,
        IBillService billService,
        CancellationToken ct)
    {
        var result = await billService.UpdateAsync(
            id, req.Name, req.CategoryId, req.Kind, req.DefaultAmount, req.SplitRatio, req.PersonId, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteBill(
        long id,
        IBillService billService,
        CancellationToken ct)
    {
        var result = await billService.DeleteByIdAsync(id, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> RecalculateBill(
        long billId,
        RecalculateRequest req,
        IBillService billService,
        CancellationToken ct)
    {
        var result = await billService.RecalculateAsync(billId, req.FromYear, req.FromMonth, req.NewAmount, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> GetBillHistory(
        long billId,
        int? fromYear,
        int? fromMonth,
        int? toYear,
        int? toMonth,
        IBillService billService,
        CancellationToken ct)
    {
        var result = await billService.GetHistoryAsync(billId, fromYear, fromMonth, toYear, toMonth, ct);

        return result.ToHttpResult();
    }
}
