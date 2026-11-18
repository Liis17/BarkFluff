using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Users;

using MassTransit;

namespace BarkFluff.Bots.Consumers;

public sealed class UserAccountStatusChangedConsumer(
    UsersServerApi.UsersServerApiClient usersClient,
    BotsStorage botsStorage,
    BotRegistryCache registryCache,
    ILogger<UserAccountStatusChangedConsumer> logger) : IConsumer<UserAccountStatusChanged>
{
    public async Task Consume(ConsumeContext<UserAccountStatusChanged> context)
    {
        var ownerId = context.Message.UserId;
        var current = await usersClient.GetAccountRestrictionAsync(
            new GetAccountRestrictionRequest { UserId = ownerId },
            deadline: DateTime.UtcNow.AddSeconds(5),
            cancellationToken: context.CancellationToken);
        var restricted = current.Restriction.Status != AccountStatus.Active;
        await botsStorage.SetOwnerRestricted(ownerId, restricted, context.CancellationToken);
        registryCache.ApplyOwnerRestriction(ownerId, restricted);
        logger.LogInformation("Боты владельца {OwnerUserId} {Status}", ownerId, restricted ? "ограничены" : "активированы");
    }
}
