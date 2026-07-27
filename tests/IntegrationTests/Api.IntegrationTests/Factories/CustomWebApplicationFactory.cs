using System.Diagnostics.CodeAnalysis;
using Data.Contexts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Respawn;

namespace Api.IntegrationTests.Factories;

[ExcludeFromCodeCoverage]
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private static string _testConnectionString = null!;

    public string TestConnectionString => _testConnectionString;

    private Respawner _respawner = null!;
    private NpgsqlConnection _dbConnection = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, cfg) =>
        {
            cfg.AddUserSecrets<CustomWebApplicationFactory>();
            cfg.AddEnvironmentVariables();
            var built = cfg.Build();

            //Get connection string for donet user-secrets
            _testConnectionString = NeonConnectionString.Normalize(
                           built.GetConnectionString("NeonTest")
                           ?? throw new InvalidOperationException(
                               "ConnectionStrings:NeonTest não configurada. Configure via dotnet user-secrets ou a env var ConnectionStrings__NeonTest."))!;

            // Firebase:ProjectId must be non-empty to satisfy ValidateOnStart; the actual
            // value is overridden in SetupJwt so Firebase is never contacted during tests.
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firebase:ProjectId"] = TestTokens.ProjectId,
            });
        });

        builder.ConfigureTestServices((services) =>
        {
            ReplaceDatabase(services);
            SetupJwt(services);
        });
        base.ConfigureWebHost(builder);
    }

    private static void ReplaceDatabase(IServiceCollection services)
    {
        var descriptors = services
            .Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || d.ServiceType == typeof(AppDbContext)
                || (d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration") == true
                    && d.ServiceType.GenericTypeArguments is [var arg] && arg == typeof(AppDbContext)))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(_testConnectionString));
    }

    private static void SetupJwt(IServiceCollection services) =>
    // Validate against the local test key instead of fetching Firebase metadata.
    services.PostConfigure<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme,
        options =>
        {
            options.Authority = null;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = TestTokens.Issuer,
                ValidateAudience = true,
                ValidAudience = TestTokens.ProjectId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = TestTokens.SigningKey,
            };
        });

    public async ValueTask ResetDatabaseAsync()
    {
        await _respawner.ResetAsync(_dbConnection);
    }

    public async Task FinalizeAsync()
    {
        await _dbConnection.DisposeAsync();
    }



    public async Task InitializeAsync()
    {

        // aplica migrations no container
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        _dbConnection = new NpgsqlConnection(TestConnectionString);
        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            SchemasToInclude = ["public"],
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }
}
