using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeleteMessage;

public class DeleteMessageCommand : IRequest<DeleteMessageResponse>
{
    public long MessageId { get; set; }

    /// <summary>
    /// Автор удаления для серверного пути (DeleteMessageServer, сервис Bots).
    /// null = клиентский путь, автор берётся из UserContext.
    /// Проверка авторства сохраняется: удалять можно только свои сообщения.
    /// </summary>
    public long? SenderId { get; set; }
}
