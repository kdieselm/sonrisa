using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sonrisa.Application.Abstractions;

namespace Sonrisa.Infrastructure.Persistence;

/// <summary>One EF Core transaction per unit of work (ADR-3).</summary>
public sealed class EfTransactionRunner(AppDbContext db) : ITransactionRunner
{
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation
                                           } pg)
        {
            // Disposing the transaction rolls it back; the failed changes must not be saved again later.
            db.ChangeTracker.Clear();
            throw new DuplicateKeyException($"Unique index '{pg.ConstraintName}' rejected the write.", ex);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
