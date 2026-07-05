using Data.Contexts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Hosts the API in-memory for integration tests, connecting to a real PostgreSQL database
/// (Neon, bills_test) and validating tokens against the local test signing key.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private string _testConnectionString = null!;

    /// <summary>Resolved Npgsql connection string for the test database.</summary>
    public string TestConnectionString => _testConnectionString;

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddUserSecrets<CustomWebApplicationFactory>();
            cfg.AddEnvironmentVariables();
            var built = cfg.Build();
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

        builder.ConfigureTestServices(services =>
        {
            ReplaceDatabase(services);
            SetupJwt(services);
        });
    }

    private void ReplaceDatabase(IServiceCollection services)
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

    private static void SetupJwt(IServiceCollection services)
    {
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
    }
}
