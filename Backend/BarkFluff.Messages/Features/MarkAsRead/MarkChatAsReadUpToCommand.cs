using MediatR;

namespace BarkFluff.Messages.Features.MarkAsRead;

public sealed record MarkChatAsReadUpToCommand(Guid ChatId, long UpToMessageId) : IRequest<bool>;
