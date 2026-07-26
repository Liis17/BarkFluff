using BarkFluff.Bots.Messages;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;

using MassTransit;

namespace BarkFluff.Bots.Consumers;

/// <summary>
/// Fan-out инвалидация кэша реестра ботов: каждый инстанс применяет изменение к своему локальному
/// кэшу — перечитывает бота из БД или удаляет. Применяет через ApplyRemote-методы (без повторной
/// публикации), чтобы не зациклиться.
/// </summary>
public class BotRegistryChangedConsumer(BotRegistryCache cache, IServiceScopeFactory scopeFactory)
    : IConsumer<BotRegistryChangedEvent>
{
    public async Task Consume(ConsumeContext<BotRegistryChangedEvent> context)
    {
        var msg = context.Message;

        if (msg.Removed)
        {
            cache.ApplyRemove(msg.BotId);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<BotsStorage>();

        var bot = await storage.GetById(msg.BotId);
        if (bot is not null)
        {
            cache.ApplySet(bot);
        }
        else
        {
            cache.ApplyRemove(msg.BotId);
        }
    }
}
