using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.GrpcServer.Tracker;
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
            var stage = "getMe";
            long? updateId = null;
            try
            {
                var me = await bot.GetIdentity(stoppingToken);
                stage = "acquire-redis-lease";
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
                        stage = "read-postgres-offset";
                        var state = await db.TelegramPollingStates.SingleOrDefaultAsync(x => x.Id == me.Id, lease.Token);
                        var offset = state?.Offset ?? 0;
                        stage = "getUpdates";
                        var updates = await bot.GetUpdates(offset, lease.Token);
                        foreach (var update in updates)
                        {
                            lease.Token.ThrowIfCancellationRequested();
                            stage = "read-update-id";
                            updateId = null;
                            var id = update.GetProperty("update_id").GetInt64();
                            if (id < offset) continue;
                            updateId = id;
                            using var updateScope = scopes.CreateScope();
                            InitializeRequestContextForTelegramUpdate(updateScope.ServiceProvider);
                            stage = "process-update";
                            await updateScope.ServiceProvider.GetRequiredService<AuthenticationService>().ProcessTelegramUpdate(update, lease.Token);
                            var updateDb = updateScope.ServiceProvider.GetRequiredService<IdentityContext>();
                            stage = "persist-postgres-offset";
                            var cursor = await updateDb.TelegramPollingStates.SingleOrDefaultAsync(x => x.Id == me.Id, lease.Token);
                            if (cursor == null) updateDb.TelegramPollingStates.Add(new TelegramPollingState { Id = me.Id, Offset = id + 1 });
                            else cursor.Offset = Math.Max(cursor.Offset, id + 1);
                            await updateDb.SaveChangesAsync(lease.Token);
                            offset = id + 1;
                            updateId = null;
                        }
                    }
                }
                finally
                {
                    var operationStage = stage;
                    stage = "release-redis-lease";
                    await lease.CancelAsync();
                    await renewal;
                    await cache.ScriptEvaluateAsync("if redis.call('GET',KEYS[1]) == ARGV[1] then return redis.call('DEL',KEYS[1]) end return 0", [key], [owner]);
                    stage = operationStage;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Bot API exception details can contain the token in its request URL.
                var details = ex.ToString();
                if (!string.IsNullOrWhiteSpace(options.BotToken))
                {
                    details = details.Replace(options.BotToken, "[REDACTED]", StringComparison.Ordinal);
                    var encodedToken = Uri.EscapeDataString(options.BotToken);
                    if (encodedToken != options.BotToken)
                        details = details.Replace(encodedToken, "[REDACTED]", StringComparison.Ordinal);
                }
                logger.LogWarning(
                    "Telegram authentication polling unavailable at {Stage} (update {UpdateId}, {ErrorType}): {ErrorDetails}; retrying",
                    stage, updateId, ex.GetType().FullName, details);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    internal static void InitializeRequestContextForTelegramUpdate(IServiceProvider services)
    {
        services.GetRequiredService<IRequestContextAccessor>().Set(new RequestContext());
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
