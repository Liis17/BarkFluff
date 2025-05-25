using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Infrastructure;

/// <summary>
/// Хелпер для определения имени S3 бакета на основе типа загружаемого файла
/// </summary>
public static class S3BucketHelper
{
    /// <summary>
    /// Словарь соответствия типов файлов и имен бакетов
    /// </summary>
    private static readonly Dictionary<UploadFileType, string> _bucketMap = new()
    {
        { UploadFileType.Unknown, "barkfluff-uploads" },
        { UploadFileType.UserAvatar, "profile-pictures" },
        { UploadFileType.MessageAttachmentDocument, "message-documents"},
        { UploadFileType.MessageAttachmentVideo, "message-videos"},
        { UploadFileType.MessageAttachmentGif, "message-videos"},
        { UploadFileType.MessageAttachmentImage, "message-images"},
        { UploadFileType.ChatPicture, "chat-pictures" },
    };

    /// <summary>
    /// Получает имя бакета для указанного типа файла
    /// </summary>
    /// <param name="fileType">Тип загружаемого файла</param>
    /// <returns>Имя бакета S3</returns>
    public static string GetBucketName(UploadFileType fileType)
    {
        return _bucketMap.TryGetValue(fileType, out var bucketName)
            ? bucketName
            : _bucketMap[UploadFileType.Unknown]; // Возвращаем бакет по умолчанию, если тип не найден
    }
}
