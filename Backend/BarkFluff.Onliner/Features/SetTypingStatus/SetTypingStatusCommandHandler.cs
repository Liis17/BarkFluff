using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.SetTypingStatus;

public class SetTypingStatusCommandHandler : IRequestHandler<SetTypingStatusCommand, SetTypingStatusResponse>
{
    private readonly UserContext _userContext;
    private readonly TypingNotifier _notifier;
    private readonly MetricsCollector _metrics;

    public SetTypingStatusCommandHandler(
        UserContext userContext,
        TypingNotifier notifier,
        MetricsCollector metrics)
    {
        _userContext = userContext;
        _notifier = notifier;
        _metrics = metrics;
    }

    public async Task<SetTypingStatusResponse> Handle(
        SetTypingStatusCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;

        // Пустой/неизвестный action трактуем как "печатает" — клиент может слать минимальный heartbeat.
        var action = request.Action == TypingAction.Unknown
            ? TypingAction.Typing
            : request.Action;

        _metrics.Increment("typing_heartbeats");

        await _notifier.NotifyTyping(request.ChatId, userId, action, cancellationToken);

        return new SetTypingStatusResponse();
    }
}
