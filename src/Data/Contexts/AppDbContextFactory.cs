using System.Diagnostics.CodeAnalysis;
using Domain.Abstractions.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Data.Contexts;

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
[ExcludeFromCodeCoverage]
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The <c>NEON_CONNECTION_STRING</c> environment variable is not set.
    /// </exception>
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NEON_CONNECTION_STRING");

        foreach (var item in args)
            Console.WriteLine(item);

#if DEBUG
        Console.WriteLine("I am in debug configuration");
        Console.WriteLine("Connect string will be override to local database");
        connectionString = @"Host=localhost;Port=5432;Database=bills_test;Username=postgres;Password=postgres";
#endif
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The NEON_CONNECTION_STRING environment variable must be set to run the EF Core design-time tooling " +
                "(for example 'dotnet ef migrations add'). Set it to the Neon connection string before running EF commands." +
                "for debug and local database, use example 'dotnet ef migrations add 'your great migration name' --project src/Data/Data.csproj --configuration Debug'.Running from projeto root folder.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(NeonConnectionString.Normalize(connectionString))
            .Options;

        return new AppDbContext(options, new CurrentOwnerForDotNetTooling());
    }
}

public sealed record CurrentOwnerForDotNetTooling : ICurrentOwner
{
    public long Id { get; private set; }

    public void SetCurrentOwnerId(long id)
        => Id = id;
}