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
