using BarkFluff.Onliner.Messages;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using MassTransit;

namespace BarkFluff.Onliner.Consumers;

/// <summary>
/// Fan-out ретрансляции индикатора набора: каждый инстанс получает копию и доставляет её своим
/// локальным подписчикам чата (<see cref="TypingSubscriptionsManager"/>). Исключение печатающего —
/// в локальном менеджере. Инстансы без подписчиков чата — no-op.
/// </summary>
public class TypingChangedConsumer(TypingNotifier notifier) : IConsumer<TypingChangedEvent>
{
    public Task Consume(ConsumeContext<TypingChangedEvent> context)
    {
        var msg = context.Message;
        return notifier.NotifyTyping(msg.ChatId, msg.UserId, (TypingAction)msg.Action, context.CancellationToken);
    }
}
