using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.AckSecretMessage;

public class AckSecretMessageCommand : IRequest<AckSecretMessageResponse>
{
    public string MessageId { get; set; } = string.Empty;
}
