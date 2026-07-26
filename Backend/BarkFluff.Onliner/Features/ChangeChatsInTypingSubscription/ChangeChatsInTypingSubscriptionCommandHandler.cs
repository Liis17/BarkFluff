using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Onliner.Features.ChangeChatsInTypingSubscription;

public class ChangeChatsInTypingSubscriptionCommandHandler
    : IRequestHandler<ChangeChatsInTypingSubscriptionCommand, ChangeChatsInTypingSubscriptionResponse>
{
    private readonly UserContext _userContext;
    private readonly TypingSubscriptionsManager _subscriptionsManager;
    private readonly ChatMembershipFilter _membershipFilter;
    private readonly ILogger<ChangeChatsInTypingSubscriptionCommandHandler> _logger;

    public ChangeChatsInTypingSubscriptionCommandHandler(
        UserContext userContext,
        TypingSubscriptionsManager subscriptionsManager,
        ChatMembershipFilter membershipFilter,
        ILogger<ChangeChatsInTypingSubscriptionCommandHandler> logger)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
        _membershipFilter = membershipFilter;
        _logger = logger;
    }

    public async Task<ChangeChatsInTypingSubscriptionResponse> Handle(
        ChangeChatsInTypingSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;

        _logger.LogDebug(
            "Updating tracked chats for user {UserId} typing subscriptions to {ChatIdsCount} chats",
            userId, request.ChatIds.Count);

        // Оставляем только чаты, где пользователь состоит (fail-closed).
        var membership = await _membershipFilter.GetMemberChatIdsAsync(
            userId, request.ChatIds, cancellationToken);

        var filteredChatIds = request.ChatIds.Where(membership.MemberChatIds.Contains).ToList();

        var updatedCount = _subscriptionsManager.UpdateAllSubscriptions(userId, filteredChatIds);

        if (updatedCount == 0)
        {
            _logger.LogWarning(
                "User {UserId} attempted to update typing subscriptions but has none active",
                userId);

            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "No active typing subscriptions found"));
        }

        _logger.LogInformation(
            "Updated {SubscriptionCount} typing subscriptions for user {UserId} with {ChatIdsCount} tracked chats",
            updatedCount, userId, request.ChatIds.Count);

        return new ChangeChatsInTypingSubscriptionResponse();
    }
}
