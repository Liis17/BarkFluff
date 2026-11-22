using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace BarkFluff.Client.Core.Services;

public static class MessageNotificationPayload
{
    public const string ArgumentName = "messageChat";

    public static string CreateXml(MessageNotification notification) =>
        new XElement("toast",
            new XAttribute("launch", ArgumentName + "=" + Encode(notification.Target)),
            new XElement("visual", new XElement("binding",
                new XAttribute("template", "ToastGeneric"),
                new XElement("text", RemoveInvalidXmlCharacters(notification.Title)),
                new XElement("text", RemoveInvalidXmlCharacters(notification.Text)))))
            .ToString(SaveOptions.DisableFormatting);

    private static string RemoveInvalidXmlCharacters(string text)
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

    private static string Encode(MessageNotificationTarget target) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(target));
}
