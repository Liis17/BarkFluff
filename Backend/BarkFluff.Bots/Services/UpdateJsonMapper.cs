using System.Text.Json;
using System.Text.Json.Serialization;

using BarkFluff.Proto.Shared;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Payload update'а бота (jsonb в BotUpdates, без update_id — он равен Id строки).
/// Telegram-like формат, тот же JSON отдаётся в HTTP getUpdates.
/// </summary>
public class BotUpdatePayload
{
    [JsonPropertyName("message")]
    public IncomingMessagePayload Message { get; set; } = new();
}

public class IncomingMessagePayload
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("chat_id")]
    public string ChatId { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public FromUserPayload From { get; set; } = new();

    [JsonPropertyName("date")]
    public long Date { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("attachments")]
    public List<AttachmentPayload> Attachments { get; set; } = [];
}

public class FromUserPayload
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;
}

public class AttachmentPayload
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("preview_url")]
    public string PreviewUrl { get; set; } = string.Empty;

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }
}

/// <summary>
/// Маппинг proto barkfluff.shared.Message → Telegram-like JSON-payload и обратно в gRPC BotUpdate.
/// </summary>
public static class UpdateJsonMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToPayloadJson(Message message, Guid chatId, long fromUserId, string fromUsername, string fromFirstName)
    {
        var payload = new BotUpdatePayload
        {
            Message = new IncomingMessagePayload
            {
                MessageId = message.Id,
                ChatId = chatId.ToString(),
                From = new FromUserPayload
                {
                    Id = fromUserId,
                    Username = fromUsername,
                    FirstName = fromFirstName,
                },
                Date = message.SentAt?.Seconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Text = message.Content?.Text ?? string.Empty,
                Attachments = message.Content?.Attachments?
                    .Where(a => a.Type != MessageAttachmentType.ForwardedMessage)
                    .Select(a => new AttachmentPayload
                    {
                        FileId = a.FileId,
                        Type = AttachmentTypeName(a.Type),
                        PreviewUrl = a.PreviewUrl,
                        FileSize = a.AttachmentSize,
                    }).ToList() ?? [],
            },
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static BotUpdatePayload ParsePayload(string json)
        => JsonSerializer.Deserialize<BotUpdatePayload>(json, JsonOptions) ?? new BotUpdatePayload();

    public static Proto.Bots.BotUpdate ToGrpcUpdate(long updateId, string payloadJson)
    {
        var payload = ParsePayload(payloadJson);
        var message = payload.Message;

        var update = new Proto.Bots.BotUpdate
        {
            UpdateId = updateId,
            Message = new Proto.Bots.BotIncomingMessage
            {
                MessageId = message.MessageId,
                ChatId = message.ChatId,
                FromUserId = message.From.Id,
                FromUsername = message.From.Username,
                FromFirstName = message.From.FirstName,
                Date = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
                    DateTimeOffset.FromUnixTimeSeconds(message.Date)),
                Text = message.Text,
            },
        };

        update.Message.Attachments.AddRange(message.Attachments.Select(a => new Proto.Bots.BotAttachment
        {
            FileId = a.FileId,
            Type = a.Type,
            PreviewUrl = a.PreviewUrl,
            FileSize = a.FileSize,
        }));

        return update;
    }

    private static string AttachmentTypeName(MessageAttachmentType type) => type switch
    {
        MessageAttachmentType.Image => "image",
        MessageAttachmentType.Video => "video",
        MessageAttachmentType.Gif => "gif",
        MessageAttachmentType.Document => "document",
        MessageAttachmentType.Audio => "audio",
        MessageAttachmentType.Voice => "voice",
        MessageAttachmentType.Sticker => "sticker",
        _ => "unknown",
    };
}
