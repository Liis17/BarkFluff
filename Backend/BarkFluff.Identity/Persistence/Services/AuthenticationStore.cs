using System.Collections.Concurrent;
using BarkFluff.Identity.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Services;

public sealed class AuthenticationStore(IdentityContext db)
{
    // Non-relational test provider only. Production serializes across instances in PostgreSQL.
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> TestLocks = new();

    public async Task<T> ForUser<T>(long userId, Func<Task<T>> action, CancellationToken ct)
        => await ExecuteForUser(userId, action, ct, Array.Empty<Type>());

    public async Task<T> ForUserPreserving<T>(long userId, Func<Task<T>> action, CancellationToken ct,
        params Type[] commitOnException)
        => await ExecuteForUser(userId, action, ct, commitOnException);

    private async Task<T> ExecuteForUser<T>(long userId, Func<Task<T>> action, CancellationToken ct,
        IReadOnlyCollection<Type> commitOnException)
    {
        if (!db.Database.IsRelational())
        {
            var gate = TestLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1));
            await gate.WaitAsync(ct);
            try
            {
                try { var result = await action(); await db.SaveChangesAsync(ct); return result; }
                catch (Exception exception) when (ShouldCommit(exception, commitOnException))
                {
                    await db.SaveChangesAsync(ct);
                    throw;
                }
            }
            finally { gate.Release(); }
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Every policy, recovery-code and challenge transition uses the same user lock.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({userId})", ct);
            try
            {
                var result = await action();
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception exception) when (ShouldCommit(exception, commitOnException))
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                throw;
            }
        });
    }

    private static bool ShouldCommit(Exception exception, IReadOnlyCollection<Type> commitOnException) =>
        commitOnException.Any(type => type.IsInstanceOfType(exception));
}
