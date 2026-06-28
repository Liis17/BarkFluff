using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckChatMembership;

public class CheckChatMembershipQuery : IRequest<CheckChatMembershipResponse>
{
    public required long UserId { get; init; }
    public required IReadOnlyList<string> ChatIds { get; init; }
}
