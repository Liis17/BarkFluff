using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Proto.Users;

namespace BarkFluff.Bots.Services;

public sealed class BotOwnerRestrictionReconciler(
    IServiceScopeFactory scopeFactory,
    BotRegistryCache registryCache,
    ILogger<BotOwnerRestrictionReconciler> logger) : BackgroundService
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(10);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await Reconcile(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ReconciliationInterval, stoppingToken);
                await Reconcile(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Сверка статусов владельцев ботов завершилась ошибкой");
            }
        }
    }

    private async Task Reconcile(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var botsStorage = scope.ServiceProvider.GetRequiredService<BotsStorage>();
        var usersClient = scope.ServiceProvider.GetRequiredService<UsersServerApi.UsersServerApiClient>();
        foreach (var ownerId in await botsStorage.GetDistinctOwnerIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var account = await usersClient.GetAccountRestrictionAsync(
                    new GetAccountRestrictionRequest { UserId = ownerId },
                    deadline: DateTime.UtcNow.AddSeconds(5),
                    cancellationToken: cancellationToken);
                var restricted = account.Restriction.Status != AccountStatus.Active;
                await botsStorage.SetOwnerRestricted(ownerId, restricted, cancellationToken);
                registryCache.ApplyOwnerRestriction(ownerId, restricted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Не удалось сверить статус владельца ботов {OwnerUserId}", ownerId);
            }
        }

        var bots = await botsStorage.GetAll();
        registryCache.Load(bots);
    }
}
