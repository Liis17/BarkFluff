using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Features.Shared;

/// <summary>
/// Единое правило «на что можно отвечать»: сообщение существует, лежит в том же чате и не удалено.
/// Живёт отдельно, потому что правило применяется дважды — при сохранении черновика
/// (<see cref="UpsertChatDraft.UpsertChatDraftCommandHandler"/>) и при отправке
/// (<see cref="SendMessage.SendMessageCommandHandler"/>). Раньше оно существовало только в черновике,
/// и выбранный там reply уезжал на сервер уже как пересылка — расхождение между двумя путями
/// было бы легко воспроизвести снова, если держать проверку в двух местах.
/// </summary>
public static class ReplyTargetValidator
{
    /// <summary>
    /// Бросает <see cref="MessageNotFoundException"/>, если ответить на это сообщение нельзя.
    /// Ответ на сообщение чужого чата намеренно неотличим от несуществующего: иначе перебором id
    /// можно было бы выяснять, какие сообщения есть на ноде.
    /// </summary>
    public static async Task ValidateAsync(MessagesStorage messagesStorage, Guid chatId, long replyToMessageId)
    {
        var target = await messagesStorage.GetMessageById(replyToMessageId);

        if (target is null || target.ChatId != chatId || target.IsDeleted)
            throw new MessageNotFoundException();
    }
}
