using Application.Abstractions.Repositories;
using Data.Contexts;
using Data.Repositories;
using Domain.Infrastructures;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

namespace Data.DependencyInjection;

public static class RegisterData
{
    public static void Register(IServiceCollection services, AppOptions options)
    {
        ResolveContexts(services, options);
        ResolveRepositores(services);
        ResolveServices(services);
    }

    private static void ResolveContexts(IServiceCollection services, AppOptions options)
    {
        // --- Database: PostgreSQL (Neon). Connection string is supplied via configuration
        // (user-secrets locally, environment / GitHub Secrets in CI/CD) and must use the
        // pooler endpoint with "SSL Mode=Require". "App:UseProdConnection" lets a local launch
        // profile point at the "NeonProd" connection string while staying in the Development
        // environment (so user-secrets keep loading); see the "prod-data" launch profile.
        var connString = options.UseProdConnection ? options.ConnectionStrings.NeonProd
                : options.ConnectionStrings.Neon;

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(NeonConnectionString.Normalize(connString)));
    }

    private static void ResolveRepositores(IServiceCollection services)
    {
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
    }

    private static void ResolveServices(IServiceCollection services) => services.AddScoped<IMigrationService, MigrationService>();
}
