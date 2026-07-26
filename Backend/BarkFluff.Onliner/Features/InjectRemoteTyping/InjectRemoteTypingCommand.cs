using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.InjectRemoteTyping;

public class InjectRemoteTypingCommand : IRequest<InjectRemoteTypingResponse>
{
    public required string ChatId { get; init; }

    public required Guid SenderUuid { get; init; }

    public required TypingAction Action { get; init; }
}
