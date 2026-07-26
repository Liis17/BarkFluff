using BarkFluff.Proto.Bots;

namespace BarkFluff.Bots.Mapping;

public static class BotMessageMapping
{
    public static SendMessageResponse ToSendMessageResponse(this Proto.Shared.Message message, string chatId) => new()
    {
        MessageId = message.Id,
        ChatId = chatId,
        SentAt = message.SentAt,
    };

    public static EditMessageResponse ToEditMessageResponse(this Proto.Shared.Message message) => new()
    {
        MessageId = message.Id,
        Text = message.Content?.Text ?? string.Empty,
        EditedAt = message.EditedAt,
    };

    /// <summary>editMessage для HTTP Bot API.</summary>
    public static object ToHttpEditResult(this Proto.Shared.Message message) => new
    {
        message_id = message.Id,
        text = message.Content?.Text ?? string.Empty,
        edited_at = message.EditedAt?.Seconds ?? 0,
    };

    /// <summary>sendMessage/sendPhoto/sendDocument для HTTP Bot API (Telegram-like JSON).</summary>
    public static object ToHttpMessageResult(this Proto.Shared.Message message, string? chatId) => new
    {
        message_id = message.Id,
        chat_id = chatId ?? string.Empty,
        date = message.SentAt?.Seconds ?? 0,
        text = message.Content?.Text ?? string.Empty,
        attachments = message.Content?.Attachments?.Select(a => new
        {
            file_id = a.FileId,
            type = a.Type.ToString().ToLowerInvariant(),
            preview_url = a.PreviewUrl,
            file_size = a.AttachmentSize,
        }),
    };
}
