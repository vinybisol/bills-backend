using BillsBackend.Api.Data;
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
/// Hosts the API in-memory for integration tests, swapping PostgreSQL for the EF Core
/// in-memory provider and validating tokens against the local test signing key.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"integration-{Guid.NewGuid()}";

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firebase:ProjectId"] = TestTokens.ProjectId,
                ["ConnectionStrings:Neon"] = "Host=unused;Database=unused;Username=unused;Password=unused",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceDatabaseWithInMemory(services);

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
        });
    }

    private void ReplaceDatabaseWithInMemory(IServiceCollection services)
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
            options.UseInMemoryDatabase(_databaseName));
    }
}
