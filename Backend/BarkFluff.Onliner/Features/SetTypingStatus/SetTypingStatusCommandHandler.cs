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
    private readonly FederatedTypingSender _federatedTyping;
    private readonly MetricsCollector _metrics;

    public SetTypingStatusCommandHandler(
        UserContext userContext,
        IPublishEndpoint publish,
        ChatMembershipFilter membershipFilter,
        FederatedTypingSender federatedTyping,
        MetricsCollector metrics)
    {
        _userContext = userContext;
        _publish = publish;
        _membershipFilter = membershipFilter;
        _federatedTyping = federatedTyping;
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

        await SendToFederationAsync(request.ChatId, membership, action, cancellationToken);

        return new SetTypingStatusResponse();
    }

    /// <summary>
    /// Продлить набор за границу ноды (этап 4.4). Локальный путь выше уже отработал и от этой
    /// ветки не зависит — федерация не может ни задержать, ни сломать локальный typing.
    /// </summary>
    private async Task SendToFederationAsync(
        string chatId,
        ChatMembershipResult membership,
        TypingAction action,
        CancellationToken cancellationToken)
    {
        // Чат локальный — подавляющее большинство вызовов. Выходим без единой аллокации.
        if (!_federatedTyping.IsConfigured
            || !membership.FederatedChats.TryGetValue(chatId, out var peers)
            || peers.Count == 0)
        {
            return;
        }

        // У отправителя нет uuid — федерировать нечего.
        if (membership.RequesterUuid is not { } senderUuid)
        {
            return;
        }

        var destinations = peers.Select(p => p.ServerName).Distinct().ToList();

        await _federatedTyping.SendAsync(chatId, senderUuid, action, destinations, cancellationToken);
    }
}
