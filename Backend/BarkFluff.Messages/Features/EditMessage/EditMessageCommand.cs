using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.EditMessage;

public class EditMessageCommand : IRequest<EditMessageResponse>
{
    public long MessageId { get; set; }

    public string? Text { get; set; }

    public List<Guid>? FileIds { get; set; }
}
