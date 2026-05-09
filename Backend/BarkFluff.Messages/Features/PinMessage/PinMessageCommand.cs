using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.PinMessage;

public class PinMessageCommand : IRequest<PinMessageResponse>
{
    public Guid ChatId { get; set; }

    public long MessageId { get; set; }
}
