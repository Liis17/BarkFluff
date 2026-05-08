using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeleteMessage;

public class DeleteMessageCommand : IRequest<DeleteMessageResponse>
{
    public long MessageId { get; set; }
}
