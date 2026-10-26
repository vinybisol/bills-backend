using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Data.Contexts;

public interface IMigrationService
{
    void RunMigration();
}

[ExcludeFromCodeCoverage]
public sealed class MigrationService(AppDbContext db) : IMigrationService
{
    public void RunMigration()
    {
        if (db.Database.IsRelational())
            db.Database.Migrate();     
    }
}