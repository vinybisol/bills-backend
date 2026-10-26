using Data.Contexts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API in-memory for integration tests, connecting to a real PostgreSQL database
/// (Neon, bills_test) and validating tokens against the local test signing key.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string LocalSettingsFileName = "appsettings.Testing.Local.json";

    private string _testConnectionString = null!;

    /// <summary>Resolved Npgsql connection string for the test database.</summary>
    public string TestConnectionString => _testConnectionString;

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            // Local runs read the git-ignored JSON copied to the test output; CI sets the
            // ConnectionStrings__Neon env var, which is added last and therefore wins.
            cfg.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, LocalSettingsFileName),
                optional: true,
                reloadOnChange: false);
            cfg.AddEnvironmentVariables();
            var built = cfg.Build();

            // Normalized like the app does, so Respawn's raw NpgsqlConnection also accepts Neon URIs.
            _testConnectionString = NeonConnectionString.Normalize(built.GetConnectionString("Neon"))
                ?? throw new InvalidOperationException(
                        $"ConnectionStrings:Neon não configurada. Crie {LocalSettingsFileName} em tests/IntegrationTests/Api.IntegrationTests (veja appsettings.Testing.Local.example.json) ou defina a env var ConnectionStrings__Neon.");

            // Firebase:ProjectId must be non-empty to satisfy ValidateOnStart; the actual
            // value is overridden in SetupJwt so Firebase is never contacted during tests.
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firebase:ProjectId"] = TestTokens.ProjectId,
                ["SharedPages:Secret"] = TestTokens.ProjectId,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            SetupJwt(services);
        });
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
}
