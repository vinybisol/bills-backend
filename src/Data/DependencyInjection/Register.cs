using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Repositories;
using Data.Contexts;
using Data.Repositories;
using Domain.Infrastructures;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Data.DependencyInjection;

[ExcludeFromCodeCoverage]
public static class RegisterData
{
    public static void Register(IServiceCollection services)
    {
        ResolveContexts(services);
        ResolveRepositores(services);
        ResolveServices(services);
    }

    private static void ResolveContexts(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((provider, opt) =>
        {
            var connectionStrings = provider.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value;
            opt.UseNpgsql(NeonConnectionString.Normalize(connectionStrings.Neon));
        });
    }

    private static void ResolveRepositores(IServiceCollection services)
    {
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<IBillRepository, BillRepository>();
        services.AddScoped<IBillEntryRepository, BillEntryRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IHealthRepository, HealthRepository>();
        services.AddScoped<IIncomeRepository, IncomeRepository>();
        services.AddScoped<IIncomeEntryRepository, IncomeEntryRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IPersonAccessLinksRepository, PersonAccessLinksRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
    }

    private static void ResolveServices(IServiceCollection services) => services.AddScoped<IMigrationService, MigrationService>();
}
