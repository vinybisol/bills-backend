using Application.Abstractions.Services;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DependencyInjection;

public static class RegisterApplications
{
    public static void Register(IServiceCollection services)
    {
        ResolveServices(services);
    }

    private static void ResolveServices(IServiceCollection services)
    {
        services.AddScoped<IAppUserService, AppUserService>();
        services.AddScoped<IBillService, BillService>();
        services.AddScoped<IBillEntryService, BillEntryService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IEntryService, EntryService>();
        services.AddScoped<IIncomeService, IncomeService>();
        services.AddScoped<IIncomeEntryService, IncomeEntryService>();
        services.AddScoped<IPersonService, PersonService>();
        services.AddScoped<IPersonAccessLinksService, PersonAccessLinksService>();
        services.AddScoped<IProjectionService, ProjectionService>();
        services.AddScoped<IReceivablesService, ReceivablesService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<ISharedPagesTokenService, SharedPagesTokenService>();
    }
}
