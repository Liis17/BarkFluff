using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.RejectSecretChatInvite;

public class RejectSecretChatInviteCommand : IRequest<RejectSecretChatInviteResponse>
{
    public string InviteId { get; set; } = string.Empty;
}
