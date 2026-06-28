using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillsBackend.Api.Data;

/// <summary>
/// Design-time factory used by the EF Core tooling (for example <c>dotnet ef migrations add</c>).
/// </summary>
/// <remarks>
/// Having an explicit factory keeps the tooling from booting the full application host, so
/// startup concerns such as applying migrations or validating configuration do not run while
/// scaffolding migrations. The connection string is taken from the <c>NEON_CONNECTION_STRING</c>
/// environment variable; if it is missing the factory fails fast rather than falling back to a
/// placeholder, so the application is never bootstrapped against an unintended database.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The <c>NEON_CONNECTION_STRING</c> environment variable is not set.
    /// </exception>
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NEON_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The NEON_CONNECTION_STRING environment variable must be set to run the EF Core design-time tooling " +
                "(for example 'dotnet ef migrations add'). Set it to the Neon connection string before running EF commands.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(NeonConnectionString.Normalize(connectionString))
            .Options;

        return new AppDbContext(options);
    }
}
