using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetChatMemberIds;

public class GetChatMemberIdsQuery : IRequest<GetChatMemberIdsResponse>
{
    public required string ChatId { get; init; }
}
