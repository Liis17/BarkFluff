using MediatR;

namespace BarkFluff.Bots.Features.SendBotMessage;

/// <summary>Отправка сообщения от имени бота (общая для gRPC SendMessage и HTTP sendMessage).</summary>
public class SendBotMessageCommand : IRequest<Proto.Shared.Message>
{
    public long BotId { get; set; }

    public string? ChatId { get; set; }

    public long? UserId { get; set; }

    public string Text { get; set; } = string.Empty;

    public List<string> FileIds { get; set; } = [];
}
