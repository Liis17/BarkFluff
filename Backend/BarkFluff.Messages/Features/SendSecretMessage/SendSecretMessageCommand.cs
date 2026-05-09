using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.SendSecretMessage;

public class SendSecretMessageCommand : IRequest<SendSecretMessageResponse>
{
    public long RecipientUserId { get; set; }

    public Guid RecipientDeviceId { get; set; }

    public byte[] Envelope { get; set; } = Array.Empty<byte>();
}
