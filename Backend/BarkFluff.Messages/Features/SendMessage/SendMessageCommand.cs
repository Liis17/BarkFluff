using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.SendMessage;

public class SendMessageCommand : IRequest<SendMessageResponse>
{
    public Guid? ChatId { get; set; }

    public long? UserId { get; set; }

    public OutgoingMessage? Message { get; set; }

    /// <summary>
    /// Отправитель для серверного пути (SendMessageServer, сервис Bots).
    /// null = клиентский путь, отправитель берётся из UserContext.
    /// В серверном пути авто-создание личного чата запрещено (бот не пишет первым).
    /// </summary>
    public long? SenderId { get; set; }
}