namespace BarkFluff.Messages.Features.SendMessage;

public class OutgoingMessage
{
    public string? Text { get; set; }

    public List<Guid>? FileIds { get; set; }

    /// <summary>
    /// Сообщение этого же чата, на которое отвечаем. Хранится ссылкой — снапшот не делается.
    /// </summary>
    public long? ReplyToMessageId { get; set; }

    /// <summary>
    /// Пересылаемые сообщения в порядке, заданном клиентом. Устаревшее одиночное
    /// <c>forwarded_message_id</c> сервис приводит к списку из одного элемента, поэтому дальше
    /// по коду ветка одна.
    /// </summary>
    public List<long>? ForwardedMessageIds { get; set; }
}
