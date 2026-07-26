using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.EditMessage;

public class EditMessageCommand : IRequest<EditMessageResponse>
{
    public long MessageId { get; set; }

    public string? Text { get; set; }

    public List<Guid>? FileIds { get; set; }

    /// <summary>
    /// Автор правки для серверного пути (EditMessageServer, сервис Bots).
    /// null = клиентский путь, автор берётся из UserContext.
    /// Проверка авторства сохраняется: редактировать можно только свои сообщения.
    /// </summary>
    public long? SenderId { get; set; }
}
