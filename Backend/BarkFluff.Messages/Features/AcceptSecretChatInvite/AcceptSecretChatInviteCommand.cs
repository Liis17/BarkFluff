using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.AcceptSecretChatInvite;

public class AcceptSecretChatInviteCommand : IRequest<AcceptSecretChatInviteResponse>
{
    public string InviteId { get; set; } = string.Empty;

    public byte[] ResponseEnvelope { get; set; } = Array.Empty<byte>();
}
