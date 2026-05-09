using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeletePrivateMessage;

public class DeletePrivateMessageCommand : IRequest<DeletePrivateMessageResponse>
{
    public long MessageId { get; set; }
}
