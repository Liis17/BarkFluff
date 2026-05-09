using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.SendPrivateMessage;

public class SendPrivateMessageCommand : IRequest<SendPrivateMessageResponse>
{
    public Guid ChatId { get; set; }

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] AssociatedData { get; set; } = Array.Empty<byte>();
}
