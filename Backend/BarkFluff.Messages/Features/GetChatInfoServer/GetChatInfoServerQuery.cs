using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetChatInfoServer;

public class GetChatInfoServerQuery : IRequest<GetChatInfoServerResponse>
{
    public required string ChatId { get; init; }
}
