namespace BarkFluff.Messages.Domain;

public class ForwardedMessageAttachment
{
    public long Id { get; set; }

    public MessageAttachmentType Type { get; set; }

    public string FileId { get; set; }

    public string? PreviewUrl { get; set; }

    public long FileSize { get; set; }

    /// <summary>
    /// Нода-владелец байтов, если форварднули federated-вложение (этап 3.3).
    /// NULL = локальный файл. Нужен проверке доступа: без него ссылку на форварднутое
    /// fed-вложение нельзя сопоставить с его origin точно.
    /// </summary>
    public string? OriginServer { get; set; }
}
