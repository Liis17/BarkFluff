using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.PostCallSystemMessage;

public class PostCallSystemMessageCommand : IRequest<PostCallSystemMessageResponse>
{
    /// <summary>Групповой чат. Если null — личный звонок (см. Caller/Callee).</summary>
    public Guid? ChatId { get; init; }

    public long? CallerUserId { get; init; }

    public long? CalleeUserId { get; init; }

    /// <summary>От кого системное сообщение (инициатор звонка).</summary>
    public required long SenderUserId { get; init; }

    public required CallSystemResult Result { get; init; }

    public long DurationSeconds { get; init; }
}
