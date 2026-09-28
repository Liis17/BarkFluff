using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeleteChat;

public class DeleteChatCommand : IRequest<DeleteChatResponse>
{
    public Guid ChatId { get; set; }

    /// <summary>
    /// Чекбокс "удалить также для {имя}". Игнорируется (клампится к false) для федеративных
    /// DM — нет протокола синхронного стирания копии на чужой ноде.
    /// </summary>
    public bool DeleteForEveryone { get; set; }
}
