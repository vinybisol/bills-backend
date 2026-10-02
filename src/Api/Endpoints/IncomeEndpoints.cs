using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class IncomeEndpoints
{
    public static RouteGroupBuilder MapIncomeEndpoints(this RouteGroupBuilder group)
    {
        var incomeGroup = group
            .MapGroup("/incomes")
            .AddEndpointFilter<UserEndpointFilter>();

        incomeGroup.MapPost("", CreateIncome);
        incomeGroup.MapGet("", ListIncomes);
        incomeGroup.MapPut("/{id:long}", UpdateIncome);
        incomeGroup.MapDelete("/{id:long}", DeleteIncome);
        return group;
    }

    private static async Task<IResult> CreateIncome(
        HttpRequest httpRequest,
        CreateIncomeRequest req,
        IIncomeService incomeService,
        CancellationToken ct)
    {
        var result = await incomeService.CreateAsync(req.Name, req.Kind, req.DefaultAmount, ct);

        if (result.IsFailure)
            return result.ToHttpResult();

        var income = result.Value;
        return Results.Created($"{httpRequest.Path.Value}/{income.Id}", income);
    }

    private static async Task<IResult> ListIncomes(
        IIncomeService incomeService,
        CancellationToken ct)
    {
        var result = await incomeService.GetAllByNameAsync(ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateIncome(
        long id,
        UpdateIncomeRequest req,
        IIncomeService incomeService,
        CancellationToken ct)
    {
        var result = await incomeService.UpdateAsync(id, req.Name, req.Kind, req.DefaultAmount, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteIncome(
        long id,
        IIncomeService incomeService,
        CancellationToken ct)
    {
        var result = await incomeService.DeleteByIdAsync(id, ct);

        return result.ToHttpResult();
    }
}
