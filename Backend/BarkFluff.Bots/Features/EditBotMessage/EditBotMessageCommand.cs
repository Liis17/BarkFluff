using MediatR;

namespace BarkFluff.Bots.Features.EditBotMessage;

public class EditBotMessageCommand : IRequest<Proto.Shared.Message>
{
    public long BotId { get; set; }

    public long MessageId { get; set; }

    public string Text { get; set; } = string.Empty;

    public List<string> FileIds { get; set; } = [];
}
