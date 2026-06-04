using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.ChangeChatsInTypingSubscription;

public class ChangeChatsInTypingSubscriptionCommand : IRequest<ChangeChatsInTypingSubscriptionResponse>
{
    public required List<long> ChatIds { get; set; }
}
