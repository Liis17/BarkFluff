using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class MessageAttachment
{
    [Key]
    public long Id { get; set; }

    public MessageAttachmentType Type { get; set; }

    public string? FileId { get; set; }

    public string? PreviewUrl { get; set; }

    public long FileSize { get; set; }

    // ---- Снапшот метаданных federated-вложения (этап 3.1, docs/rearch/06-files.md) ----
    // Файлы не реплицируются: байты живут только на origin-ноде. Реплицируется снапшот
    // метаданных, чтобы сообщение рендерилось без единого сетевого похода на чужую ноду.

    /// <summary>NULL = локальный файл (существующее поведение); NOT NULL = байты на origin-ноде.</summary>
    public string? OriginServer { get; set; }

    /// <summary>Снапшот имени файла для поиска и рендера без дополнительного похода в Files.</summary>
    public string? FileName { get; set; }

    public string? PreviewFileId { get; set; }

    public int? ImageWidth { get; set; }

    public int? ImageHeight { get; set; }

    public string? ForwardedAuthorName { get; set; }

    public long? ForwardedOriginalMessageId { get; set; }

    public string? ForwardedText { get; set; }

    public List<ForwardedMessageAttachment>? ForwardedAttachments { get; set; }

    // ---- Обогащение снапшота пересылки (разделение reply/forward) ----
    // Без этих полей пересылка не могла показать «переслано из ⟨чат⟩ ⟨дата⟩» и перейти к оригиналу.
    // NULL у снапшотов, созданных до разделения — legacy рендерится по-старому.

    public Guid? ForwardedOriginalChatId { get; set; }

    public long? ForwardedOriginalSenderId { get; set; }

    public DateTime? ForwardedOriginalSentAt { get; set; }

    /// <summary>Порядок внутри пересылки нескольких сообщений одним сообщением.</summary>
    public int? ForwardedOrder { get; set; }
}
