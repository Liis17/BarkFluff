using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Shared.Exceptions.Messages;

using ProtoOutgoingMessage = BarkFluff.Proto.Messages.OutgoingMessage;

namespace BarkFluff.Messages.Mapping;

public static class OutgoingMessageMapping
{
    /// <summary>
    /// Приводит proto-запрос к доменной команде, разводя устаревшее одиночное
    /// <c>forwarded_message_id</c> и новые <c>reply_to_message_id</c>/<c>forwarded_message_ids</c>.
    ///
    /// До разделения reply/forward клиенты отправляли ОБА действия полем 3, поэтому поле остаётся
    /// рабочим: iOS, macOS, ClientV2.WPF и Linux ещё на нём. Старый путь трактуется как пересылка —
    /// именно так он и хранился, и переобъявить его ответом задним числом нельзя: в БД пересылка и
    /// ответ выглядят одинаково.
    /// </summary>
    public static OutgoingMessage ToCommandMessage(this ProtoOutgoingMessage message)
    {
        var legacyForwardId = message.ForwardedMessageId == 0 ? null : (long?)message.ForwardedMessageId;
        var replyToMessageId = message.ReplyToMessageId == 0 ? null : (long?)message.ReplyToMessageId;
        var forwardedMessageIds = message.ForwardedMessageIds.Count > 0
            ? message.ForwardedMessageIds.ToList()
            : null;

        // Смешивать старое поле с новыми нельзя: иначе непонятно, что имел в виду клиент —
        // и молча выбранная за него трактовка была бы хуже явной ошибки.
        if (legacyForwardId.HasValue && (replyToMessageId.HasValue || forwardedMessageIds is not null))
            throw new ConflictingForwardFieldsException();

        return new OutgoingMessage
        {
            Text = message.Text,
            FileIds = message.FilesIds?.Select(Guid.Parse).ToList(),
            ReplyToMessageId = replyToMessageId,
            ForwardedMessageIds = forwardedMessageIds ?? (legacyForwardId.HasValue ? [legacyForwardId.Value] : null)
        };
    }
}
