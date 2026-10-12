using Api.Extensions;
using Application.Abstractions.Services;
using Domain.Abstractions.Filters;

namespace Api.Endpoints;

internal static class SharedBillsEndpoint
{
    public static RouteGroupBuilder MapSharedBillsEndpoint(this RouteGroupBuilder groupBuilder)
    {
        var group = groupBuilder
        .MapGroup("/bills/shared")
        .AllowAnonymous();

        group.MapGet("", GetSharedBills);

        return groupBuilder;
    }

    private static async Task<IResult> GetSharedBills(
        string? token,
        IPersonAccessLinksService personAccessLinksService,
        IReceivablesService receivablesService,
        ICurrentOwner currentOwner,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var validateResult = await personAccessLinksService.ValidateTokenAsync(token, ct);

        if (validateResult.IsFailure)
            return validateResult.ToHttpResult();

        var validate = validateResult.Value;
        currentOwner.SetCurrentOwnerId(validate.OwnerId);

        var now = timeProvider.GetUtcNow();
        var year = now.Year;
        var month = now.Month;

        var result = await receivablesService.GetMonthByPersonAsync(year, month, validate.PersonId, ct);

        return result.ToHttpResult();
    }
}
