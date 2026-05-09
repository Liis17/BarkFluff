using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.SendSecretChatInvite;

public class SendSecretChatInviteCommand : IRequest<SendSecretChatInviteResponse>
{
    public long RecipientUserId { get; set; }

    public Guid RecipientDeviceId { get; set; }

    public byte[] InitialEnvelope { get; set; } = Array.Empty<byte>();
}
