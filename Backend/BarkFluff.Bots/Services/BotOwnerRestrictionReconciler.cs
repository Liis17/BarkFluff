using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Proto.Users;

namespace BarkFluff.Bots.Services;

public sealed class BotOwnerRestrictionReconciler(
    IServiceScopeFactory scopeFactory,
    BotRegistryCache registryCache,
    ILogger<BotOwnerRestrictionReconciler> logger) : BackgroundService
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(1);
    private const int OwnersPerRequest = 500;

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
        var ownerIds = await botsStorage.GetDistinctOwnerIds();
        if (ownerIds.Count > 0)
        {
            try
            {
                var accountsById = new Dictionary<long, User>();
                foreach (var chunk in ownerIds.Chunk(OwnersPerRequest))
                {
                    var response = await usersClient.ListByIdsAsync(
                        new ListByIdsRequest { Ids = { chunk } },
                        deadline: DateTime.UtcNow.AddSeconds(5),
                        cancellationToken: cancellationToken);
                    foreach (var user in response.Users)
                        accountsById[user.Id] = user;
                }

                foreach (var ownerId in ownerIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!accountsById.TryGetValue(ownerId, out var account))
                    {
                        logger.LogWarning("Не найден владелец ботов {OwnerUserId} при сверке статуса", ownerId);
                        continue;
                    }

                    await botsStorage.SetOwnerRestricted(ownerId, account.AccountStatus != AccountStatus.Active, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Не удалось сверить статусы владельцев ботов");
            }
        }

        var bots = await botsStorage.GetAll();
        registryCache.Load(bots);
    }
}
