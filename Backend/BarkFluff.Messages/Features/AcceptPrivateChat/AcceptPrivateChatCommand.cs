using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.AcceptPrivateChat;

public class AcceptPrivateChatCommand : IRequest<AcceptPrivateChatResponse>
{
    public Guid ChatId { get; set; }
}
