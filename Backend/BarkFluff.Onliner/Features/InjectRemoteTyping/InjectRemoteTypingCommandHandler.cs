using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Messages;
using BarkFluff.Proto.Onliner;

using MassTransit;

using MediatR;

namespace BarkFluff.Onliner.Features.InjectRemoteTyping;

/// <summary>
/// Federation вливает набор remote-пользователя (этап 4.2). Как и локальный typing —
/// fan-out'ом: ретранслируют подписчикам чата все инстансы. Ничего не персистится.
/// Авторство и членство проверяет Federation до вызова (этап 4.4).
/// </summary>
public class InjectRemoteTypingCommandHandler
    : IRequestHandler<InjectRemoteTypingCommand, InjectRemoteTypingResponse>
{
    private readonly IPublishEndpoint _publish;
    private readonly MetricsCollector _metrics;

    public InjectRemoteTypingCommandHandler(IPublishEndpoint publish, MetricsCollector metrics)
    {
        _publish = publish;
        _metrics = metrics;
    }

    public async Task<InjectRemoteTypingResponse> Handle(
        InjectRemoteTypingCommand request,
        CancellationToken cancellationToken)
    {
        _metrics.Increment("remote_typing_injections");

        // Пустой/неизвестный action трактуем как "печатает" — так же, как на локальном пути.
        var action = request.Action == TypingAction.Unknown ? TypingAction.Typing : request.Action;

        await _publish.Publish(new TypingChangedEvent
        {
            ChatId = request.ChatId,
            UserId = 0,
            UserUuid = request.SenderUuid,
            Action = (int)action,
        }, cancellationToken);

        return new InjectRemoteTypingResponse();
    }
}
