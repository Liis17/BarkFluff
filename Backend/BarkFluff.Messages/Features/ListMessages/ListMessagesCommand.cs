using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ListMessages;

public class ListMessagesCommand : IRequest<ListMessagesResponse>
{
    public Guid ChatId { get; set; }

    public long FromMessageId { get; set; }

    public int Count { get; set; }

    /// <summary>
    /// Количество сообщений до указанного (в прошлое)
    /// </summary>
    public int OffsetBefore { get; set; }

    /// <summary>
    /// Количество сообщений после указанного (в будущее)
    /// </summary>
    public int OffsetAfter { get; set; }
}