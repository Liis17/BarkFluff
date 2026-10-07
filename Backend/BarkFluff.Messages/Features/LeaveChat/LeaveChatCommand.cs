using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.LeaveChat;

public class LeaveChatCommand : IRequest<LeaveChatResponse>
{
    public Guid ChatId { get; set; }
}
