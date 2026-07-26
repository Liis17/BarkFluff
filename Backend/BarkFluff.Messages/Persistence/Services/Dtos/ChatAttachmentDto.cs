using BarkFluff.Messages.Domain;

namespace BarkFluff.Messages.Persistence.Services.Dtos;

public class ChatAttachmentDto
{
    public long MessageId { get; set; }
    public long SenderId { get; set; }
    public DateTime SentAt { get; set; }
    public long AttachmentId { get; set; }
    public MessageAttachmentType AttachmentType { get; set; }
    public string FileId { get; set; }
    public string? PreviewUrl { get; set; }
    public long FileSize { get; set; }

    // Снапшот federated-вложения (этап 3.1). OriginServer != null → метаданные берутся отсюда,
    // а не из Files: файла на этой ноде нет.
    public string? OriginServer { get; set; }
    public string? FileName { get; set; }
    public string? PreviewFileId { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
}
