using MediatR;

namespace BarkFluff.Messages.Features.MarkPrivateMessagesAsRead;

public class MarkPrivateMessagesAsReadCommand : IRequest
{
    public Guid ChatId { get; init; }

    public long LastReadMessageId { get; init; }
}
