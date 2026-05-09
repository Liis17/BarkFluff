using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CreatePrivateChat;

public class CreatePrivateChatCommand : IRequest<CreatePrivateChatResponse>
{
    public long PeerUserId { get; set; }

    public byte[] KdfSalt { get; set; } = Array.Empty<byte>();

    public byte[] PassphraseVerifier { get; set; } = Array.Empty<byte>();
}
