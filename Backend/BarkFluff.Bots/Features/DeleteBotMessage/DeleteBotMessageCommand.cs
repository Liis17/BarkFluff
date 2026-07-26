using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.DeleteBotMessage;

public class DeleteBotMessageCommand : IRequest<DeleteMessageResponse>
{
    public long BotId { get; set; }

    public long MessageId { get; set; }
}
