using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.UnpinMessage;

public class UnpinMessageCommand : IRequest<UnpinMessageResponse>
{
    public Guid ChatId { get; set; }

    public long MessageId { get; set; }
}
