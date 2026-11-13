using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using Microsoft.EntityFrameworkCore;

using StackExchange.Redis;

namespace BarkFluff.Identity.Infrastructure;

public sealed class AccountDeletionWorker(
    IServiceScopeFactory scopeFactory,
    IConnectionMultiplexer redis,
    ILogger<AccountDeletionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly RedisKey LeaseKey = "identity:account-deletion-worker";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLeasedCycle(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Цикл окончательного удаления аккаунтов завершился ошибкой");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunLeasedCycle(CancellationToken stoppingToken)
    {
        var database = redis.GetDatabase();
        var owner = Guid.NewGuid().ToString("N");
        if (!await database.StringSetAsync(LeaseKey, owner, LeaseDuration, When.NotExists))
            return;

        using var lease = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewal = RenewLease(database, owner, lease);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UsersServerApi.UsersServerApiClient>();
            var claims = await users.ClaimAccountsForDeletionAsync(new ClaimAccountsForDeletionRequest { Limit = 100 },
                cancellationToken: lease.Token);

            foreach (var userId in claims.UserIds)
            {
                lease.Token.ThrowIfCancellationRequested();
                await CleanupUser(userId, lease.Token);
            }
        }
        finally
        {
            await lease.CancelAsync();
            await renewal;
            await database.ScriptEvaluateAsync(
                "if redis.call('GET',KEYS[1]) == ARGV[1] then return redis.call('DEL',KEYS[1]) end return 0",
                [LeaseKey], [owner]);
        }
    }

    private async Task CleanupUser(long userId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
            await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
            {
                await context.RefreshTokens.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await context.AuthUserProperties.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await context.ConfirmationCodes.Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
                await context.UserPasswords.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await context.AuthenticationChallenges.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await context.RecoveryCodes.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await context.ResetPasswords.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            var files = scope.ServiceProvider.GetRequiredService<FilesServerApi.FilesServerApiClient>();
            await files.DeleteUserProfileImagesAsync(new DeleteUserProfileImagesRequest { UserId = userId },
                cancellationToken: cancellationToken);

            var bots = scope.ServiceProvider.GetRequiredService<BotsServerApi.BotsServerApiClient>();
            await bots.DeleteBotsForOwnerAsync(new DeleteBotsForOwnerRequest { OwnerUserId = userId },
                cancellationToken: cancellationToken);

            var users = scope.ServiceProvider.GetRequiredService<UsersServerApi.UsersServerApiClient>();
            await users.CompleteAccountCleanupAsync(new CompleteAccountCleanupRequest { UserId = userId },
                cancellationToken: cancellationToken);
            logger.LogInformation("Окончательная очистка аккаунта {UserId} завершена", userId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Очистка аккаунта {UserId} не завершена; повторится в следующем цикле", userId);
        }
    }

    private static async Task RenewLease(IDatabase database, string owner, CancellationTokenSource lease)
    {
        try
        {
            while (!lease.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), lease.Token);
                var renewed = (long)await database.ScriptEvaluateAsync(
                    "if redis.call('GET',KEYS[1]) == ARGV[1] then return redis.call('EXPIRE',KEYS[1],120) end return 0",
                    [LeaseKey], [owner]);
                if (renewed == 0)
                {
                    await lease.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (RedisException) { await lease.CancelAsync(); }
    }
}
