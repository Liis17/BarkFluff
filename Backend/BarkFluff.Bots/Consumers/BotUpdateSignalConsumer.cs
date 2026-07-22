using BarkFluff.Bots.Messages;
using BarkFluff.Bots.Services;

using MassTransit;

namespace BarkFluff.Bots.Consumers;

/// <summary>
/// Fan-out доставка сигнала о новом update: каждый инстанс будит свои локальные long-poll/стрим
/// waiter'ы. Инстансы без активного poller'а этого бота — no-op.
/// </summary>
public class BotUpdateSignalConsumer(BotUpdateNotifier notifier) : IConsumer<BotUpdateSignalEvent>
{
    public Task Consume(ConsumeContext<BotUpdateSignalEvent> context)
    {
        notifier.Signal(context.Message.BotId);
        return Task.CompletedTask;
    }
}
