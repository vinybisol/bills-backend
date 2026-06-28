using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillsBackend.Api.Data;

/// <summary>
/// Design-time factory used by the EF Core tooling (for example <c>dotnet ef migrations add</c>).
/// </summary>
/// <remarks>
/// Having an explicit factory keeps the tooling from booting the full application host, so
/// startup concerns such as applying migrations or validating configuration do not run while
/// scaffolding migrations. The connection string is read from the <c>NEON_CONNECTION_STRING</c>
/// environment variable when present and otherwise falls back to a placeholder, since generating
/// a migration never opens a connection.
/// </remarks>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("NEON_CONNECTION_STRING")
            ?? "Host=localhost;Database=bills;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
