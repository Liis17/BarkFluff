using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.DeleteBot;

public class DeleteBotCommand : IRequest<DeleteBotResponse>
{
    public long BotId { get; set; }
}
