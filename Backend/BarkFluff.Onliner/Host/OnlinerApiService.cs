using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Features.ChangeUsersInSubscription;
using BarkFluff.Onliner.Features.GetOnlineStatus;
using BarkFluff.Onliner.Features.SetOnlineStatus;
using BarkFluff.Onliner.Features.SubscribeToOnlineStatus;
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
    private readonly MetricsCollector _metrics;

    public OnlinerApiService(
        IMediator mediator,
        SubscribeToOnlineStatusQueryHandler subscribeHandler,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _subscribeHandler = subscribeHandler;
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
            UserIds = request.UserIds.ToList()
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
            UserIds = request.UserIds.ToList()
        };

        return await _mediator.Send(command, context.CancellationToken);
    }
}
