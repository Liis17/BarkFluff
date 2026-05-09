using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.EditPrivateMessage;

public class EditPrivateMessageCommand : IRequest<EditPrivateMessageResponse>
{
    public long MessageId { get; set; }

    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    public byte[] AssociatedData { get; set; } = Array.Empty<byte>();
}
