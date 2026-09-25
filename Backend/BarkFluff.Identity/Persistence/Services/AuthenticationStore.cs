using System.Collections.Concurrent;
using BarkFluff.Identity.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Services;

public sealed class AuthenticationStore(IdentityContext db)
{
    // Non-relational test provider only. Production serializes across instances in PostgreSQL.
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> TestLocks = new();

    public async Task<T> ForUser<T>(long userId, Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var gate = TestLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1));
            await gate.WaitAsync(ct);
            try { var result = await action(); await db.SaveChangesAsync(ct); return result; }
            finally { gate.Release(); }
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Every policy, recovery-code and challenge transition uses the same user lock.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({userId})", ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
