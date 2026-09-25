using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Services;
using BarkFluff.Identity.Settings;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace BarkFluff.Identity.Infrastructure;

public sealed class TelegramAuthWorker(IServiceScopeFactory scopes, ITelegramAuthBot bot, TelegramAuthOptions options,
    IConnectionMultiplexer redis, ILogger<TelegramAuthWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Configured) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var me = await bot.GetIdentity(stoppingToken);
                var key = (RedisKey)$"identity:telegram-poller:{me.Id}";
                var owner = Guid.NewGuid().ToString("N");
                var cache = redis.GetDatabase();
                if (!await cache.StringSetAsync(key, owner, TimeSpan.FromSeconds(60), When.NotExists))
                { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                using var lease = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var renewal = Renew(cache, key, owner, lease);
                try
                {
                    while (!lease.IsCancellationRequested)
                    {
                        using var scope = scopes.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                        var state = await db.TelegramPollingStates.SingleOrDefaultAsync(x => x.Id == me.Id, lease.Token);
                        var offset = state?.Offset ?? 0;
                        var updates = await bot.GetUpdates(offset, lease.Token);
                        foreach (var update in updates)
                        {
                            lease.Token.ThrowIfCancellationRequested();
                            var id = update.GetProperty("update_id").GetInt64();
                            if (id < offset) continue;
                            using var updateScope = scopes.CreateScope();
                            await updateScope.ServiceProvider.GetRequiredService<AuthenticationService>().ProcessTelegramUpdate(update, lease.Token);
                            var updateDb = updateScope.ServiceProvider.GetRequiredService<IdentityContext>();
                            var cursor = await updateDb.TelegramPollingStates.SingleOrDefaultAsync(x => x.Id == me.Id, lease.Token);
                            if (cursor == null) updateDb.TelegramPollingStates.Add(new TelegramPollingState { Id = me.Id, Offset = id + 1 });
                            else cursor.Offset = Math.Max(cursor.Offset, id + 1);
                            await updateDb.SaveChangesAsync(lease.Token);
                            offset = id + 1;
                        }
                    }
                }
                finally
                {
                    await lease.CancelAsync();
                    await renewal;
                    await cache.ScriptEvaluateAsync("if redis.call('GET',KEYS[1]) == ARGV[1] then return redis.call('DEL',KEYS[1]) end return 0", [key], [owner]);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // HTTP exceptions can contain bot-token URLs. Only the type is safe to log.
                logger.LogWarning("Telegram authentication polling unavailable ({ErrorType}); retrying", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static async Task Renew(IDatabase cache, RedisKey key, string owner, CancellationTokenSource lease)
    {
        try
        {
            while (!lease.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), lease.Token);
                var renewed = (long)await cache.ScriptEvaluateAsync(
                    "if redis.call('GET',KEYS[1]) == ARGV[1] then return redis.call('EXPIRE',KEYS[1],60) end return 0", [key], [owner]);
                if (renewed == 0) { await lease.CancelAsync(); return; }
            }
        }
        catch (OperationCanceledException) { }
        catch (RedisException) { await lease.CancelAsync(); }
    }
}
