using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Messages;

using MassTransit;

namespace BarkFluff.Federation.Consumers;

/// <summary>
/// Изменения статусов локальных пользователей для отдачи нодам-партнёрам (этап 4.3).
/// </summary>
/// <remarks>
/// Onliner уже публикует <see cref="OnlineStatusChangedEvent"/> fan-out'ом ради межинстансной
/// доставки — Federation заводит свою per-instance очередь и слушает те же события. Отдельный
/// долгоживущий gRPC-стрим Onliner → Federation не нужен вовсе.
///
/// Консюмер только помечает пользователя «грязным»: сам статус перечитывается у Onliner в момент
/// отправки (<c>GetLocalPresence</c>, там же применяется privacy). Поэтому здесь нет ни privacy-логики,
/// ни риска отдать в стрим состояние из устаревшего события.
/// </remarks>
public class PresenceStatusChangedConsumer : IConsumer<OnlineStatusChangedEvent>
{
    private readonly IncomingPresenceRegistry _registry;
    private readonly FederationSwitch _switch;
    private readonly MetricsCollector _metrics;

    public PresenceStatusChangedConsumer(
        IncomingPresenceRegistry registry,
        FederationSwitch federationSwitch,
        MetricsCollector metrics)
    {
        _registry = registry;
        _switch = federationSwitch;
        _metrics = metrics;
    }

    public Task Consume(ConsumeContext<OnlineStatusChangedEvent> context)
    {
        if (!_switch.IsActive)
        {
            return Task.CompletedTask;
        }

        // Событие про remote-пользователя — оно и пришло из федерации. Пересылать его обратно
        // нельзя: нода говорит только за своих.
        if (context.Message.UserUuid.HasValue)
        {
            return Task.CompletedTask;
        }

        _registry.MarkStatusChanged(context.Message.UserId);
        _metrics.Increment("presence_local_changes_observed");

        return Task.CompletedTask;
    }
}
