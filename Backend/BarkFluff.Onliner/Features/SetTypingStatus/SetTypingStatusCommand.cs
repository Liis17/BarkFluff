using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.SetTypingStatus;

public class SetTypingStatusCommand : IRequest<SetTypingStatusResponse>
{
    public required long ChatId { get; set; }
    public required TypingAction Action { get; set; }
}
