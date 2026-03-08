using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.ChangeUsersInSubscription;

public class ChangeUsersInSubscriptionCommand : IRequest<ChangeUsersInSubscriptionResponse>
{
    public required List<long> UserIds { get; init; }
}
