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

    public OnlinerApiService(
        IMediator mediator,
        SubscribeToOnlineStatusQueryHandler subscribeHandler)
    {
        _mediator = mediator;
        _subscribeHandler = subscribeHandler;
    }

    public override Task<GetOnlineStatusResponse> GetOnlineStatus(
        GetOnlineStatusRequest request,
        ServerCallContext context)
    {
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
        var command = new SetOnlineStatusCommand();

        return _mediator.Send(command, context.CancellationToken);
    }

    public override Task SubscribeToOnlineStatus(
        SubscribeToOnlineStatusRequest request,
        IServerStreamWriter<UserOnlineStatus> responseStream,
        ServerCallContext context)
    {
        var query = new SubscribeToOnlineStatusQuery
        {
            UserIds = request.UserIds.ToList(),
            ResponseStream = responseStream,
            CancellationToken = context.CancellationToken
        };

        // Не используем MediatR для streaming - прямой вызов handler
        return _subscribeHandler.Handle(query);
    }

    public override async Task<ChangeUsersInSubscriptionResponse> ChangeUsersInSubscription(
        ChangeUsersInSubscriptionRequest request,
        ServerCallContext context)
    {
        var command = new ChangeUsersInSubscriptionCommand
        {
            UserIds = request.UserIds.ToList()
        };

        return await _mediator.Send(command, context.CancellationToken);
    }
}