using Application.Abstractions.Exceptions;
using Application.Abstractions.Repositories;
using Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Data.Repositories;

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
              when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
        {
            throw new UniqueConstraintViolationException(pg.ConstraintName, ex);
        }
    }
}