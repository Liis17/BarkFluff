using BarkFluff.Proto.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.GetChatInfo;

public class GetChatInfoCommand : IRequest<GetChatInfoResponse>
{
    public Guid ChatId { get; set; }
}
