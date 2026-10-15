using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        var dashboardGroup = group
            .MapGroup("/dashboard")
            .AddEndpointFilter<UserEndpointFilter>();

        dashboardGroup.MapGet("/month", GetDashboardMonth);
        dashboardGroup.MapGet("/year", GetDashboardYear);
        return group;
    }

    private static async Task<IResult> GetDashboardMonth(
        int? year,
        int? month,
        IDashboardService dashboardService,
        CancellationToken ct)
    {
        var result = await dashboardService.GetMonthAsync(year, month, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> GetDashboardYear(
        int? year,
        IDashboardService dashboardService,
        CancellationToken ct)
    {
        var result = await dashboardService.GetYearAsync(year, ct);

        return result.ToHttpResult();
    }
}
