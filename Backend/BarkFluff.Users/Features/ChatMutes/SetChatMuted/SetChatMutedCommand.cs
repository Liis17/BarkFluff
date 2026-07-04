using MediatR;

namespace BarkFluff.Users.Features.ChatMutes.SetChatMuted;

public class SetChatMutedCommand : IRequest<Unit>
{
    public Guid ChatId { get; set; }

    public bool Muted { get; set; }

    // null при Muted=true => навсегда.
    public DateTime? MutedUntil { get; set; }
}
