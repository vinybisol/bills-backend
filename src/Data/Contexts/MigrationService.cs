using Domain.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Data.Contexts;

public interface IMigrationService
{
    void RunMigration(AppOptions options);
}

public sealed class MigrationService(AppDbContext db) : IMigrationService
{
    public void RunMigration(AppOptions options)
    {
        // Never auto-migrate when pointed at the production connection string — schema
        // changes against prod go through the deploy pipeline only.
        if (!options.UseProdConnection)
        {
            if (db.Database.IsRelational())
                db.Database.Migrate();
        }
    }
}