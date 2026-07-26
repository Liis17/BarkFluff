using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetMyCommands;

public class GetMyCommandsQuery : IRequest<GetMyCommandsResponse>
{
    public long BotId { get; set; }
}
