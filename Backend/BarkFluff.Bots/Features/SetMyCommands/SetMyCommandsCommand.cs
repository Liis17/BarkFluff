using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.SetMyCommands;

public class SetMyCommandsCommand : IRequest<SetMyCommandsResponse>
{
    public long BotId { get; set; }

    public List<Domain.BotCommand> Commands { get; set; } = [];
}
