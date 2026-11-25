using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace BarkFluff.Client.Core.Services;

public static class MessageNotificationPayload
{
    public const string ArgumentName = "messageChat";
    public const string ReplyOperationArgument = "replyOperation";
    public const string ReplyMessageArgument = "replyMessage";

    public static string CreateXml(MessageNotification notification) =>
        new XElement("toast",
            new XAttribute("launch", ArgumentName + "=" + Encode(notification.Target)),
            new XElement("visual", new XElement("binding",
                new XAttribute("template", "ToastGeneric"),
                new XElement("text", RemoveInvalidXmlCharacters(notification.Title)),
                new XElement("text", RemoveInvalidXmlCharacters(notification.Text)))))
            .ToString(SaveOptions.DisableFormatting);

    public static string RemoveInvalidXmlCharacters(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsSurrogatePair(text, index))
            {
                result.Append(text, index, 2);
                index++;
            }
            else if (XmlConvert.IsXmlChar(text[index]))
            {
                result.Append(text[index]);
            }
        }
        return result.ToString();
    }

    // Ключ группы ограничен по длине и не содержит адрес ноды или идентификаторы.
    public static string Group(MessageNotificationTarget target) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(target)).AsSpan(0, 16));

    public static MessageNotificationTarget? Parse(string argument)
    {
        try
        {
            var target = JsonSerializer.Deserialize<MessageNotificationTarget>(Base64Url.DecodeFromChars(argument));
            return target is { Node.Length: > 0, UserId: > 0, ChatId.Length: > 0 } ? target : null;
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            return null;
        }
    }

    public static (MessageNotificationTarget? Target, NotificationReply? Reply) ParseActivation(
        IDictionary<string, string> arguments, string? text)
    {
        var target = arguments.TryGetValue(ArgumentName, out var encoded) ? Parse(encoded) : null;
        if (target is null || !arguments.TryGetValue(ReplyOperationArgument, out var operation))
            return (target, null);

        Guid.TryParseExact(operation, "N", out var operationId);
        arguments.TryGetValue(ReplyMessageArgument, out var message);
        long.TryParse(message, NumberStyles.Integer, CultureInfo.InvariantCulture, out var messageId);
        return (target, new NotificationReply(target, operationId, messageId, text ?? string.Empty));
    }

    public static string Encode(MessageNotificationTarget target) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(target));
}
