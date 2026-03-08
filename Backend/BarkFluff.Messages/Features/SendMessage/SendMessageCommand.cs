using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.SendMessage;

public class SendMessageCommand : IRequest<SendMessageResponse>
{
    public Guid? ChatId { get; set; }

    public long? UserId { get; set; }

    public OutgoingMessage? Message { get; set; }

}