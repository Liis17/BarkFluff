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
    private readonly ChatMembershipFilter _membershipFilter;
    private readonly MetricsCollector _metrics;

    public SetTypingStatusCommandHandler(
        UserContext userContext,
        TypingNotifier notifier,
        ChatMembershipFilter membershipFilter,
        MetricsCollector metrics)
    {
        _userContext = userContext;
        _notifier = notifier;
        _membershipFilter = membershipFilter;
        _metrics = metrics;
    }

    public async Task<SetTypingStatusResponse> Handle(
        SetTypingStatusCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;

        _metrics.Increment("typing_heartbeats");

        // Не-участник не может инжектить набор в чужой чат (fail-closed).
        var memberChatIds = await _membershipFilter.GetMemberChatIdsAsync(
            userId, [request.ChatId], cancellationToken);

        if (!memberChatIds.Contains(request.ChatId))
        {
            _metrics.Increment("typing_heartbeats_rejected_by_membership");
            return new SetTypingStatusResponse();
        }

        // Пустой/неизвестный action трактуем как "печатает" — клиент может слать минимальный heartbeat.
        var action = request.Action == TypingAction.Unknown
            ? TypingAction.Typing
            : request.Action;

        await _notifier.NotifyTyping(request.ChatId, userId, action, cancellationToken);

        return new SetTypingStatusResponse();
    }
}
