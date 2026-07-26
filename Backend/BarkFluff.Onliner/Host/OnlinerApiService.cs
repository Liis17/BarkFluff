using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Features.ChangeChatsInTypingSubscription;
using BarkFluff.Onliner.Features.ChangeUsersInSubscription;
using BarkFluff.Onliner.Features.GetOnlineStatus;
using BarkFluff.Onliner.Features.SetOnlineStatus;
using BarkFluff.Onliner.Features.SetTypingStatus;
using BarkFluff.Onliner.Features.SubscribeToOnlineStatus;
using BarkFluff.Onliner.Features.SubscribeToTyping;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Onliner.Host;

[Authorize(Policy = nameof(TokenType.User))] // Все методы требуют User токен
public class OnlinerApiService : OnlinerApi.OnlinerApiBase
{
    private readonly IMediator _mediator;
    private readonly SubscribeToOnlineStatusQueryHandler _subscribeHandler;
    private readonly SubscribeToTypingQueryHandler _typingSubscribeHandler;
    private readonly MetricsCollector _metrics;

    public OnlinerApiService(
        IMediator mediator,
        SubscribeToOnlineStatusQueryHandler subscribeHandler,
        SubscribeToTypingQueryHandler typingSubscribeHandler,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _subscribeHandler = subscribeHandler;
        _typingSubscribeHandler = typingSubscribeHandler;
        _metrics = metrics;
    }

    public override Task<GetOnlineStatusResponse> GetOnlineStatus(
        GetOnlineStatusRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("get_online_status_requests");
        _metrics.Add("get_online_status_user_ids_total", request.UserIds.Count);

        var query = new GetOnlineStatusQuery
        {
            UserIds = request.UserIds.ToList(),
            UserUuids = ParseTrackedUuids(request.UserUuids)
        };

        return _mediator.Send(query, context.CancellationToken);
    }

    public override Task<SetOnlineStatusResponse> SetOnlineStatus(
        SetOnlineStatusRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("set_online_status_requests");

        var command = new SetOnlineStatusCommand();

        return _mediator.Send(command, context.CancellationToken);
    }

    public override async Task SubscribeToOnlineStatus(
        SubscribeToOnlineStatusRequest request,
        IServerStreamWriter<UserOnlineStatus> responseStream,
        ServerCallContext context)
    {
        // active_subscriptions поддерживается как gauge через MetricsSnapshotService.
        // Здесь считаем только factual events: новые подключения и отключения.
        _metrics.Increment("subscribe_requests");

        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = request.UserIds.ToList(),
            UserUuids = ParseTrackedUuids(request.UserUuids),
            ResponseStream = responseStream,
            CancellationToken = context.CancellationToken
        };

        // Не используем MediatR для streaming - прямой вызов handler
        await _subscribeHandler.Handle(query);
    }

    public override async Task<ChangeUsersInSubscriptionResponse> ChangeUsersInSubscription(
        ChangeUsersInSubscriptionRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("change_users_in_subscription_requests");

        var command = new ChangeUsersInSubscriptionCommand
        {
            UserIds = request.UserIds.ToList(),
            UserUuids = ParseTrackedUuids(request.UserUuids)
        };

        return await _mediator.Send(command, context.CancellationToken);
    }

    public override Task<SetTypingStatusResponse> SetTypingStatus(
        SetTypingStatusRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("set_typing_status_requests");

        var command = new SetTypingStatusCommand
        {
            ChatId = request.ChatId,
            Action = request.Action
        };

        return _mediator.Send(command, context.CancellationToken);
    }

    public override async Task SubscribeToTyping(
        SubscribeToTypingRequest request,
        IServerStreamWriter<TypingEvent> responseStream,
        ServerCallContext context)
    {
        _metrics.Increment("typing_subscribe_requests");

        var query = new SubscribeToTypingQuery
        {
            ChatIds = request.ChatIds.ToList(),
            ResponseStream = responseStream,
            CancellationToken = context.CancellationToken
        };

        // Не используем MediatR для streaming - прямой вызов handler
        await _typingSubscribeHandler.Handle(query);
    }

    public override async Task<ChangeChatsInTypingSubscriptionResponse> ChangeChatsInTypingSubscription(
        ChangeChatsInTypingSubscriptionRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("change_chats_in_typing_subscription_requests");

        var command = new ChangeChatsInTypingSubscriptionCommand
        {
            ChatIds = request.ChatIds.ToList()
        };

        return await _mediator.Send(command, context.CancellationToken);
    }

    /// <summary>
    /// Разобрать remote-uuid подписки (этап 4.2). Невалидный элемент отбрасывается молча,
    /// превышение лимита — <c>InvalidArgument</c>: подписка неограниченного размера —
    /// вектор ресурсного злоупотребления.
    /// </summary>
    private static List<Guid> ParseTrackedUuids(IReadOnlyCollection<string> rawUuids)
    {
        if (rawUuids.Count > PresenceUuids.MaxSubscriptionUuids)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"user_uuids: не более {PresenceUuids.MaxSubscriptionUuids} за вызов"));
        }

        return PresenceUuids.Parse(rawUuids);
    }
}
