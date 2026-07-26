using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Messages;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using MassTransit;

using MediatR;

namespace BarkFluff.Onliner.Features.SetTypingStatus;

public class SetTypingStatusCommandHandler : IRequestHandler<SetTypingStatusCommand, SetTypingStatusResponse>
{
    private readonly UserContext _userContext;
    private readonly IPublishEndpoint _publish;
    private readonly ChatMembershipFilter _membershipFilter;
    private readonly MetricsCollector _metrics;

    public SetTypingStatusCommandHandler(
        UserContext userContext,
        IPublishEndpoint publish,
        ChatMembershipFilter membershipFilter,
        MetricsCollector metrics)
    {
        _userContext = userContext;
        _publish = publish;
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
        var membership = await _membershipFilter.GetMemberChatIdsAsync(
            userId, [request.ChatId], cancellationToken);

        if (!membership.MemberChatIds.Contains(request.ChatId))
        {
            _metrics.Increment("typing_heartbeats_rejected_by_membership");
            return new SetTypingStatusResponse();
        }

        // Пустой/неизвестный action трактуем как "печатает" — клиент может слать минимальный heartbeat.
        var action = request.Action == TypingAction.Unknown
            ? TypingAction.Typing
            : request.Action;

        // Fan-out: набор ретранслируют подписчикам чата все инстансы (стрим подписчика может жить на другом).
        await _publish.Publish(new TypingChangedEvent
        {
            ChatId = request.ChatId,
            UserId = userId,
            Action = (int)action,
        }, cancellationToken);

        return new SetTypingStatusResponse();
    }
}
