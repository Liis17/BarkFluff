using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.RejectPrivateChat;

public class RejectPrivateChatCommand : IRequest<RejectPrivateChatResponse>
{
    public Guid ChatId { get; set; }
}
