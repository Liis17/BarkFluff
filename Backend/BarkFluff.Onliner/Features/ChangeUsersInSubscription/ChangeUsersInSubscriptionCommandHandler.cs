using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;
using Grpc.Core;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Onliner.Features.ChangeUsersInSubscription;

public class ChangeUsersInSubscriptionCommandHandler
    : IRequestHandler<ChangeUsersInSubscriptionCommand, ChangeUsersInSubscriptionResponse>
{
    private readonly UserContext _userContext;
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<ChangeUsersInSubscriptionCommandHandler> _logger;

    public ChangeUsersInSubscriptionCommandHandler(
        UserContext userContext,
        OnlineStatusSubscriptionsManager subscriptionsManager,
        ILogger<ChangeUsersInSubscriptionCommandHandler> logger)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
        _logger = logger;
    }

    public async Task<ChangeUsersInSubscriptionResponse> Handle(
        ChangeUsersInSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;

        _logger.LogDebug(
            "Updating tracked users for user {UserId} subscriptions to {UserIdsCount} users",
            userId, request.UserIds.Count);

        var updatedCount = _subscriptionsManager.UpdateAllSubscriptions(userId, request.UserIds);

        if (updatedCount == 0)
        {
            _logger.LogWarning(
                "User {UserId} attempted to update subscriptions but has none active",
                userId);

            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "No active subscriptions found"));
        }

        _logger.LogInformation(
            "Updated {SubscriptionCount} subscriptions for user {UserId} with {UserIdsCount} tracked users",
            updatedCount, userId, request.UserIds.Count);

        return new ChangeUsersInSubscriptionResponse();
    }
}
